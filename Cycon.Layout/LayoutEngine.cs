using System.Collections.Generic;
using Cycon.Core;
using Cycon.Core.Scrolling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scene3D;
using Cycon.Layout.Scrolling;
using Cycon.Layout.Wrapping;
using Cycon.Render;

namespace Cycon.Layout;

public sealed class LayoutEngine
{
    public LayoutFrame Layout(ConsoleDocument document, LayoutSettings settings, ConsoleViewport viewport)
    {
        var grid = FixedCellGrid.FromViewport(viewport, settings);
        var lines = new List<LayoutLine>();
        var hitLines = new List<HitTestLine>();
        var sceneViewports = new List<Scene3DViewportLayout>();

        var rowIndex = 0;
        for (var blockIndex = 0; blockIndex < document.Transcript.Blocks.Count; blockIndex++)
        {
            var block = document.Transcript.Blocks[blockIndex];
            if (block.Kind == BlockKind.Scene3D)
            {
                var desiredHeight = (int)Math.Round(grid.ContentWidthPx / (16.0 / 9.0));
                if (block is IMeasureBlock measure)
                {
                    desiredHeight = Math.Max(1, measure.Measure(new BlockMeasureContext(grid.ContentWidthPx, grid.CellWidthPx, grid.CellHeightPx, grid.Rows)).HeightPx);
                }

                var maxHeight = Math.Max(grid.CellHeightPx, grid.ContentHeightPx - (grid.CellHeightPx * 2));
                desiredHeight = Math.Min(desiredHeight, maxHeight);

                var viewportRect = Scene3DLayouter.LayoutViewport(grid, rowIndex, desiredHeightPx: desiredHeight);
                var aspect = desiredHeight > 0 ? (grid.ContentWidthPx / (double)desiredHeight) : 0;
                sceneViewports.Add(new Scene3DViewportLayout(block.Id, blockIndex, rowIndex, viewportRect, aspect));

                var consumedRows = Math.Max(1, (viewportRect.Height + grid.CellHeightPx - 1) / grid.CellHeightPx);
                for (var r = 0; r < consumedRows; r++)
                {
                    lines.Add(new LayoutLine(block.Id, blockIndex, Start: 0, Length: 0, rowIndex + r));
                    hitLines.Add(new HitTestLine(block.Id, blockIndex, Start: 0, Length: 0, rowIndex + r));
                }

                rowIndex += consumedRows;
                continue;
            }

            var text = GetBlockText(block);
            var wrapped = LineWrapper.Wrap(text, grid.Cols);

            foreach (var line in wrapped)
            {
                lines.Add(new LayoutLine(block.Id, blockIndex, line.Start, line.Length, rowIndex));
                hitLines.Add(new HitTestLine(block.Id, blockIndex, line.Start, line.Length, rowIndex));
                rowIndex++;
            }
        }

        var hitMap = new HitTestMap(grid, hitLines);
        var maxScrollOffsetRows = grid.Rows <= 0 ? 0 : Math.Max(0, rowIndex - grid.Rows);
        var clampedScrollOffsetRows = Math.Clamp(document.Scroll.ScrollOffsetRows, 0, maxScrollOffsetRows);
        var scrollbar = ScrollbarLayouter.Layout(grid, rowIndex, clampedScrollOffsetRows, document.Settings.Scrollbar);
        return new LayoutFrame(grid, lines, hitMap, rowIndex, scrollbar, sceneViewports);
    }

    private static string GetBlockText(IBlock block)
    {
        return block switch
        {
            TextBlock textBlock => textBlock.Text,
            ActivityBlock activityBlock => activityBlock.ExportText(0, activityBlock.TextLength),
            PromptBlock promptBlock => promptBlock.Prompt + promptBlock.Input,
            ImageBlock => throw new NotSupportedException("ImageBlock layout not implemented in Blocks v0."),
            Scene3DBlock => throw new NotSupportedException("Scene3DBlock layout not implemented in Blocks v0."),
            _ => string.Empty
        };
    }
}
