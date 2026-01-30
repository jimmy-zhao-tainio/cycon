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
using Cycon.Layout.Scrolling;
using Cycon.Rendering.Commands;
using Cycon.Rendering.Styling;
using Cycon.Layout.Overlays;

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
        UIActionState uiActions = default,
        OverlaySlabFrame? overlaySlab = null,
        UIActionState overlayActions = default,
        bool renderMuted = false)
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
        var selectionForRender = renderMuted ? null : selection;
        var selectionBackground = selectionStyle.SelectedBackgroundRgba;

        var fontMetrics = font.Metrics;
        var indicatorsSettings = document.Settings.Indicators;
        var indicatedCommandBlocks = new HashSet<BlockId>();

        var canvas = new RenderCanvas(frame, font);
        var nextSceneViewportIndex = 0;

        // Modal backdrop blur: capture the whole background into an offscreen texture and blur it,
        // then draw the modal slab on top fully sharp.
        var wantsModalBackdropBlur = overlaySlab is { IsModal: true };
        long modalBackdropCacheKey = 0;
        if (wantsModalBackdropBlur)
        {
            modalBackdropCacheKey = ComputeModalBackdropCacheKey(document, layout);
            frame.Add(new BeginModalBackdropBlur(modalBackdropCacheKey));
        }

        CaretPass.CaretQuad? pendingCaret = null;

        var defaultFg = document.Settings.DefaultTextStyle.ForegroundRgba;
        var defaultBg = document.Settings.DefaultTextStyle.BackgroundRgba;

        var selectedSpan = renderMuted ? null : TryFindSpan(layout, uiActions.FocusedId);
        var pressedSpan = renderMuted ? null : TryFindSpan(layout, uiActions.PressedId);
        var hoveredSpan = renderMuted ? null : TryFindSpan(layout, uiActions.HoveredId);

        // Inverted highlight for clickable entries: bg becomes fg, text becomes bg.
        if (selectedSpan is { } selectedSpanValue)
        {
            AddActionSpanHighlight(frame, selectedSpanValue, scrollYPx, layout.Grid, contentHighlightWidthPx, defaultFg);
        }

        // Pressed uses a slightly stronger background while keeping normal text color.
        if (pressedSpan is { } p)
        {
            if (selectedSpan is null || p != selectedSpan.Value)
            {
                var pressedBg = unchecked((int)0x303030FF);
                AddActionSpanHighlight(frame, p, scrollYPx, layout.Grid, contentHighlightWidthPx, pressedBg);
            }
        }

        if (hoveredSpan is { } h)
        {
            // Don't double-layer when the same segment is already selected/pressed.
            if ((selectedSpan is null || h != selectedSpan.Value) &&
                (pressedSpan is null || h != pressedSpan.Value))
            {
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

        if (selectionForRender is { } s && selectionBackground.HasValue)
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
            selectionForRender,
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

        // Focus-stealing viewports should push the whole background back without changing per-glyph colors.
        // Keep consistent with the modal scrub layer (blur uses scrim lightly; focus uses scrim only).
        const int focusScrimRgba = unchecked((int)0x00000055);

        if (!wantsModalBackdropBlur && focusedViewportBlockId is not null)
        {
            // In-transcript focused viewports "own" input; dim everything else using the same scrim,
            // but keep the focused block undimmed so focus is obvious.
            var focusRect = TryGetFocusedViewportRectOnScreen(layout, focusedViewportBlockId.Value, scrollYPx);
            AddScrimWithHole(frame, grid.FramebufferWidthPx, grid.FramebufferHeightPx, focusScrimRgba, focusRect);
        }

        if (wantsModalBackdropBlur)
        {
            frame.Add(new EndModalBackdropBlur());
            frame.Add(new DrawModalBackdropBlur(modalBackdropCacheKey));

            // Light scrim on top of blurred background to compress contrast slightly; blur is the primary cue.
            const int modalBlurScrimRgba = unchecked((int)0x00000055);
            frame.Add(new DrawQuad(0, 0, grid.FramebufferWidthPx, grid.FramebufferHeightPx, modalBlurScrimRgba));
        }

        if (overlaySlab is not null)
        {
            OverlaySlabPass.Render(
                canvas,
                grid,
                overlaySlab,
                overlayActions,
                foregroundRgba: defaultFg,
                backgroundRgba: defaultBg);
        }

        return frame;
    }

    private static long ComputeModalBackdropCacheKey(ConsoleDocument document, LayoutFrame layout)
    {
        // Constant-time key for caching blurred backdrops while a modal overlay is open.
        // Focus/hover state is suppressed under modals, so omit UIActionState and caret.
        unchecked
        {
            var blocks = document.Transcript.Blocks;
            var count = blocks.Count;
            var last0 = count > 0 ? blocks[^1].Id.Value : 0;
            var last1 = count > 1 ? blocks[^2].Id.Value : 0;
            var last2 = count > 2 ? blocks[^3].Id.Value : 0;

            long h = 1469598103934665603L; // FNV-1a 64-bit offset basis
            h = Fnv1a(h, (ulong)layout.Grid.FramebufferWidthPx);
            h = Fnv1a(h, (ulong)layout.Grid.FramebufferHeightPx);
            h = Fnv1a(h, (ulong)layout.TotalRows);
            h = Fnv1a(h, (ulong)document.Scroll.ScrollOffsetPx);
            h = Fnv1a(h, (ulong)count);
            h = Fnv1a(h, (ulong)last0);
            h = Fnv1a(h, (ulong)last1);
            h = Fnv1a(h, (ulong)last2);
            if (h == 0) h = 1;
            return h;
        }
    }

    private static long Fnv1a(long h, ulong data)
    {
        unchecked
        {
            ulong u = (ulong)h;
            u ^= (byte)data;
            u *= 1099511628211UL;
            u ^= (byte)(data >> 8);
            u *= 1099511628211UL;
            u ^= (byte)(data >> 16);
            u *= 1099511628211UL;
            u ^= (byte)(data >> 24);
            u *= 1099511628211UL;
            u ^= (byte)(data >> 32);
            u *= 1099511628211UL;
            u ^= (byte)(data >> 40);
            u *= 1099511628211UL;
            u ^= (byte)(data >> 48);
            u *= 1099511628211UL;
            u ^= (byte)(data >> 56);
            u *= 1099511628211UL;
            return (long)u;
        }
    }

    private static PxRect? TryGetFocusedViewportRectOnScreen(LayoutFrame layout, BlockId focusedBlockId, int scrollYPx)
    {
        var viewports = layout.Scene3DViewports;
        for (var i = 0; i < viewports.Count; i++)
        {
            var v = viewports[i];
            if (v.BlockId != focusedBlockId)
            {
                continue;
            }

            var r = v.ViewportRectPx;
            return new PxRect(r.X, r.Y - scrollYPx, r.Width, r.Height);
        }

        return null;
    }

    private static void AddScrimWithHole(RenderFrame frame, int screenW, int screenH, int scrimRgba, PxRect? holeRect)
    {
        if (screenW <= 0 || screenH <= 0)
        {
            return;
        }

        if (holeRect is not { } hole || hole.Width <= 0 || hole.Height <= 0)
        {
            frame.Add(new DrawQuad(0, 0, screenW, screenH, scrimRgba));
            return;
        }

        var hx0 = Math.Clamp(hole.X, 0, screenW);
        var hy0 = Math.Clamp(hole.Y, 0, screenH);
        var hx1 = Math.Clamp(hole.X + hole.Width, 0, screenW);
        var hy1 = Math.Clamp(hole.Y + hole.Height, 0, screenH);

        if (hx0 >= hx1 || hy0 >= hy1)
        {
            frame.Add(new DrawQuad(0, 0, screenW, screenH, scrimRgba));
            return;
        }

        // Top band.
        if (hy0 > 0)
        {
            frame.Add(new DrawQuad(0, 0, screenW, hy0, scrimRgba));
        }

        // Bottom band.
        if (hy1 < screenH)
        {
            frame.Add(new DrawQuad(0, hy1, screenW, screenH - hy1, scrimRgba));
        }

        var midH = hy1 - hy0;
        if (midH <= 0)
        {
            return;
        }

        // Left band.
        if (hx0 > 0)
        {
            frame.Add(new DrawQuad(0, hy0, hx0, midH, scrimRgba));
        }

        // Right band.
        if (hx1 < screenW)
        {
            frame.Add(new DrawQuad(hx1, hy0, screenW - hx1, midH, scrimRgba));
        }
    }

    private static HitTestActionSpan? TryFindSpan(LayoutFrame layout, UIActionId? id)
    {
        if (id is null || id.Value.IsEmpty)
        {
            return null;
        }

        var spans = layout.HitTestMap.ActionSpans;
        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (UIActionFactory.GetId(span) == id.Value)
            {
                return span;
            }
        }

        return null;
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
