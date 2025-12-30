using System.Collections.Generic;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout.Wrapping;

namespace Cycon.Layout.Blocks;

public static class TextBlockLayouter
{
    public static IReadOnlyList<LineSpan> Wrap(TextBlock block, int columns)
    {
        return LineWrapper.Wrap(block.Text, columns);
    }
}
