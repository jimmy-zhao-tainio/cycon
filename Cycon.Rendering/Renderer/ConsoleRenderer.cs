using System;
using System.Collections.Generic;
using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Metrics;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Rendering.Commands;
using Cycon.Rendering.Styling;

namespace Cycon.Rendering.Renderer;

public sealed class ConsoleRenderer
{
    public RenderFrame Render(
        ConsoleDocument document,
        LayoutFrame layout,
        IConsoleFont font,
        SelectionStyle selectionStyle,
        double timeSeconds,
        IReadOnlyDictionary<BlockId, BlockId>? commandIndicators = null,
        byte caretAlpha = 0xFF,
        IReadOnlyList<int>? meshReleases = null,
        BlockId? focusedViewportBlockId = null,
        BlockId? selectedActionSpanBlockId = null,
        string? selectedActionSpanCommandText = null,
        int selectedActionSpanIndex = -1,
        bool hasMousePosition = false,
        int mouseX = 0,
        int mouseY = 0)
    {
        var frame = new RenderFrame
        {
            BuiltGrid = new GridSize(layout.Grid.Cols, layout.Grid.Rows)
        };

        if (meshReleases is not null)
        {
            for (var i = 0; i < meshReleases.Count; i++)
            {
                frame.Add(new ReleaseMesh3D(meshReleases[i]));
            }
        }

        var grid = layout.Grid;
        var blocks = document.Transcript.Blocks;
        var scrollOffsetPx = GetScrollOffsetPx(document, layout);
        var cellH = grid.CellHeightPx;
        var scrollOffsetRows = cellH <= 0 ? 0 : scrollOffsetPx / cellH;
        var scrollRemainderPx = cellH <= 0 ? 0 : scrollOffsetPx - (scrollOffsetRows * cellH);
        var scrollYPx = scrollOffsetPx;
        var bottomSpillPx = grid.FramebufferHeightPx - (grid.PaddingTopPx + (grid.Rows * cellH));
        var allowExtraRow = scrollRemainderPx != 0 || bottomSpillPx > 0;
        var scrollbarClipRightX = layout.Scrollbar.IsScrollable ? layout.Scrollbar.TrackRectPx.X : grid.PaddingLeftPx + grid.ContentWidthPx;
        var contentHighlightWidthPx = Math.Max(0, scrollbarClipRightX - grid.PaddingLeftPx);

        var selection = SelectionPass.ComputeSelectionBounds(document);
        var selectionBackground = selectionStyle.SelectedBackgroundRgba;

        var fontMetrics = font.Metrics;
        var indicatorsSettings = document.Settings.Indicators;
        var indicatedCommandBlocks = new HashSet<BlockId>();

        var canvas = new RenderCanvas(frame, font);
        var nextSceneViewportIndex = 0;

        CaretPass.CaretQuad? pendingCaret = null;

        var defaultFg = document.Settings.DefaultTextStyle.ForegroundRgba;
        var defaultBg = document.Settings.DefaultTextStyle.BackgroundRgba;

        HitTestActionSpan? selectedSpan = null;
        if (selectedActionSpanIndex >= 0 &&
            selectedActionSpanIndex < layout.HitTestMap.ActionSpans.Count &&
            selectedActionSpanBlockId is { } selBlock &&
            !string.IsNullOrEmpty(selectedActionSpanCommandText))
        {
            var candidate = layout.HitTestMap.ActionSpans[selectedActionSpanIndex];
            if (candidate.BlockId == selBlock && candidate.CommandText == selectedActionSpanCommandText)
            {
                selectedSpan = candidate;
            }
        }

        if (selectedSpan is null &&
            selectedActionSpanBlockId is { } selectedBlock &&
            !string.IsNullOrEmpty(selectedActionSpanCommandText))
        {
            foreach (var span in layout.HitTestMap.ActionSpans)
            {
                if (span.BlockId == selectedBlock && span.CommandText == selectedActionSpanCommandText)
                {
                    selectedSpan = span;
                    break;
                }
            }
        }

        HitTestActionSpan? hoveredSpan = null;
        if (hasMousePosition &&
            (!layout.Scrollbar.IsScrollable || (!layout.Scrollbar.HitTrackRectPx.Contains(mouseX, mouseY) && mouseX < layout.Scrollbar.TrackRectPx.X)) &&
            TryGetActionSpanIndexOnRow(layout, mouseX, mouseY + scrollYPx, out var hoveredIndex) &&
            hoveredIndex >= 0 &&
            hoveredIndex < layout.HitTestMap.ActionSpans.Count)
        {
            hoveredSpan = layout.HitTestMap.ActionSpans[hoveredIndex];
        }

        // Inverted highlight for clickable entries: bg becomes fg, text becomes bg.
        if (selectedSpan is { } selectedSpanValue)
        {
            AddActionSpanHighlight(frame, selectedSpanValue, scrollYPx, layout.Grid, contentHighlightWidthPx, defaultFg);
        }
        if (hoveredSpan is { } h)
        {
            // Don't double-layer when the same segment is both selected and hovered.
            if (selectedSpan is null || h != selectedSpan.Value)
            {
                // Hover uses a darker background while keeping normal text color.
                var hoverBg = unchecked((int)0x202020FF);
                AddActionSpanHighlight(frame, h, scrollYPx, layout.Grid, contentHighlightWidthPx, hoverBg);
            }
        }

        var lines = layout.Lines;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];

            ViewportBlockPass.RenderViewportsStartingAtRow(
                canvas,
                document,
                layout,
                fontMetrics,
                scrollYPx,
                line.RowIndex,
                ref nextSceneViewportIndex,
                timeSeconds,
                focusedViewportBlockId);

            var rowOnScreen = line.RowIndex - scrollOffsetRows;
            var lineY = grid.PaddingTopPx + (rowOnScreen * cellH) - scrollRemainderPx;
            if (cellH <= 0 ||
                lineY >= grid.FramebufferHeightPx ||
                lineY + cellH <= 0)
            {
                continue;
            }

            if ((uint)line.BlockIndex >= (uint)blocks.Count)
            {
                continue;
            }

            var block = blocks[line.BlockIndex];
            var text = TextPass.GetBlockText(block);
            if (line.Start + line.Length > text.Length)
            {
                continue;
            }

            var lineForeground = TextPass.GetBlockForeground(document.Settings, block);

            if (commandIndicators is not null && block is TextBlock)
            {
                ActivityIndicatorPass.MaybeAddIndicator(
                    frame,
                    blocks,
                    lines,
                    lineIndex,
                    line,
                    line.Length,
                    commandIndicators,
                    indicatedCommandBlocks,
                    indicatorsSettings,
                    fontMetrics,
                    grid,
                    rowOnScreen,
                    scrollRemainderPx,
                    lineForeground,
                    timeSeconds);
            }

            if (block is PromptBlock prompt && pendingCaret is null)
            {
                pendingCaret = CaretPass.TryComputeCaret(prompt, layout, line.BlockIndex);
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (selection is { } s && selectionBackground.HasValue)
            {
                SelectionPass.AddSelectionBackgroundRuns(
                    frame,
                    block,
                    s,
                    line,
                    grid,
                    rowOnScreen,
                    scrollRemainderPx,
                    selectionBackground.Value);
            }

            TextPass.AddGlyphRun(
                frame,
                font,
                fontMetrics,
                grid,
                line,
                rowOnScreen,
                scrollRemainderPx,
                block,
                text,
                lineForeground,
                selection,
                selectionStyle.SelectedForegroundRgba,
                hoveredActionSpan: null,
                selectedActionSpan: selectedSpan,
                invertedTextRgba: defaultBg);
        }

        if (pendingCaret is { } caretQuad)
        {
            var caretColor = document.Settings.DefaultTextStyle.ForegroundRgba;
            CaretPass.RenderCaret(frame, grid, fontMetrics, scrollOffsetPx, caretQuad, caretColor, caretAlpha);
        }

        return frame;
    }

    private static int GetScrollOffsetPx(ConsoleDocument document, LayoutFrame layout)
    {
        var maxScrollOffsetRows = layout.Grid.Rows <= 0
            ? 0
            : Math.Max(0, layout.TotalRows - layout.Grid.Rows);

        var cellH = layout.Grid.CellHeightPx;
        var maxScrollOffsetPx = Math.Max(0, maxScrollOffsetRows * cellH);
        return Math.Clamp(document.Scroll.ScrollOffsetPx, 0, maxScrollOffsetPx);
    }

    private static void AddActionSpanHighlight(RenderFrame frame, in HitTestActionSpan span, int scrollYPx, in FixedCellGrid grid, int widthPx, int rgba)
    {
        var rect = span.RectPx;
        var x = grid.PaddingLeftPx;
        var y = rect.Y - scrollYPx;
        var w = widthPx;
        var h = grid.CellHeightPx;

        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (y >= grid.FramebufferHeightPx || y + h <= 0)
        {
            return;
        }

        frame.Add(new DrawQuad(x, y, w, h, rgba));
    }

    private static bool TryGetActionSpanIndexOnRow(LayoutFrame layout, int pixelX, int pixelY, out int spanIndex)
    {
        var map = layout.HitTestMap;
        if (map.TryGetActionAt(pixelX, pixelY, out spanIndex))
        {
            return true;
        }

        spanIndex = -1;
        var grid = layout.Grid;
        var cellH = grid.CellHeightPx;
        if (cellH <= 0)
        {
            return false;
        }

        var localY = pixelY - grid.PaddingTopPx;
        if (localY < 0)
        {
            return false;
        }

        var row = localY / cellH;
        var rowY = grid.PaddingTopPx + (row * cellH);

        var best = -1;
        var bestDist = int.MaxValue;
        var spans = map.ActionSpans;
        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            var r = span.RectPx;
            if (r.Y > rowY)
            {
                break;
            }

            if (r.Y != rowY)
            {
                continue;
            }

            var dist = 0;
            if (pixelX < r.X) dist = r.X - pixelX;
            else if (pixelX >= r.X + r.Width) dist = pixelX - (r.X + r.Width);

            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
                if (dist == 0)
                {
                    break;
                }
            }
        }

        if (best >= 0)
        {
            spanIndex = best;
            return true;
        }

        return false;
    }
}
