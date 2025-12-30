using System.Collections.Generic;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout.Wrapping;

namespace Cycon.Layout.Blocks;

public static class PromptBlockLayouter
{
    public static IReadOnlyList<LineSpan> Wrap(PromptBlock block, int columns)
    {
        return LineWrapper.Wrap(block.PromptText, columns);
    }
}
