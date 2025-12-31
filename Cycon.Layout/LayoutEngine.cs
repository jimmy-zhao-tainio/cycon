using System.Collections.Generic;
using Cycon.Core;
using Cycon.Core.Scrolling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;
using Cycon.Layout.Wrapping;

namespace Cycon.Layout;

public sealed class LayoutEngine
{
    public LayoutFrame Layout(ConsoleDocument document, LayoutSettings settings, ConsoleViewport viewport)
    {
        var grid = FixedCellGrid.FromViewport(viewport, settings);
        var lines = new List<LayoutLine>();
        var hitLines = new List<HitTestLine>();

        var rowIndex = 0;
        for (var blockIndex = 0; blockIndex < document.Transcript.Blocks.Count; blockIndex++)
        {
            var block = document.Transcript.Blocks[blockIndex];
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
        return new LayoutFrame(grid, lines, hitMap, rowIndex, scrollbar);
    }

    private static string GetBlockText(IBlock block)
    {
        return block switch
        {
            TextBlock textBlock => textBlock.Text,
            PromptBlock promptBlock => promptBlock.Prompt + promptBlock.Input,
            ImageBlock => throw new NotSupportedException("ImageBlock layout not implemented in Blocks v0."),
            Scene3DBlock => throw new NotSupportedException("Scene3DBlock layout not implemented in Blocks v0."),
            _ => string.Empty
        };
    }
}
