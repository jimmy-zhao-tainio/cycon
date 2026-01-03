using System;
using System.Collections.Generic;
using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Metrics;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
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
        IReadOnlyList<int>? meshReleases = null)
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
        var scrollOffsetRows = GetScrollOffsetRows(document, layout);
        var scrollYPx = scrollOffsetRows * grid.CellHeightPx;

        var selection = SelectionPass.ComputeSelectionBounds(document);
        var selectionBackground = selectionStyle.SelectedBackgroundRgba;

        var fontMetrics = font.Metrics;
        var indicatorsSettings = document.Settings.Indicators;
        var indicatedCommandBlocks = new HashSet<BlockId>();

        var canvas = new RenderCanvas(frame, font);
        var nextSceneViewportIndex = 0;

        CaretPass.CaretQuad? pendingCaret = null;

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
                timeSeconds);

            var rowOnScreen = line.RowIndex - scrollOffsetRows;
            if (rowOnScreen < 0 || rowOnScreen >= grid.Rows)
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
                    selectionBackground.Value);
            }

            TextPass.AddGlyphRun(
                frame,
                font,
                fontMetrics,
                grid,
                line,
                rowOnScreen,
                block,
                text,
                lineForeground,
                selection,
                selectionStyle.SelectedForegroundRgba);
        }

        if (pendingCaret is { } caretQuad)
        {
            var caretColor = document.Settings.DefaultTextStyle.ForegroundRgba;
            CaretPass.RenderCaret(frame, grid, fontMetrics, scrollOffsetRows, caretQuad, caretColor, caretAlpha);
        }

        return frame;
    }

    private static int GetScrollOffsetRows(ConsoleDocument document, LayoutFrame layout)
    {
        var maxScrollOffsetRows = layout.Grid.Rows <= 0
            ? 0
            : Math.Max(0, layout.TotalRows - layout.Grid.Rows);

        return Math.Clamp(document.Scroll.ScrollOffsetRows, 0, maxScrollOffsetRows);
    }
}
