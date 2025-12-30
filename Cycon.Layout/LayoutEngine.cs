using System.Collections.Generic;
using System.Text;
using Cycon.Core;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
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
                lines.Add(new LayoutLine(blockIndex, line.Start, line.Length, rowIndex));
                hitLines.Add(new HitTestLine(blockIndex, line.Start, line.Length, rowIndex));
                rowIndex++;
            }
        }

        var hitMap = new HitTestMap(grid, hitLines);
        return new LayoutFrame(grid, lines, hitMap, rowIndex);
    }

    private static string GetBlockText(IBlock block)
    {
        return block switch
        {
            TextBlock textBlock => Concatenate(textBlock),
            PromptBlock promptBlock => promptBlock.PromptText,
            _ => string.Empty
        };
    }

    private static string Concatenate(TextBlock block)
    {
        if (block.Spans.Count == 1)
        {
            return block.Spans[0].Text;
        }

        var builder = new StringBuilder();
        foreach (var span in block.Spans)
        {
            builder.Append(span.Text);
        }

        return builder.ToString();
    }
}
