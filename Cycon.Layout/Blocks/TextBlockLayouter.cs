using System.Collections.Generic;
using System.Text;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout.Wrapping;

namespace Cycon.Layout.Blocks;

public static class TextBlockLayouter
{
    public static IReadOnlyList<LineSpan> Wrap(TextBlock block, int columns)
    {
        var text = Concatenate(block);
        return LineWrapper.Wrap(text, columns);
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
