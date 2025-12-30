using System.Collections.Generic;
using Cycon.Core.Styling;

namespace Cycon.Core.Transcript.Blocks;

public sealed class TextBlock : IBlock
{
    public TextBlock(IReadOnlyList<TextSpan> spans)
    {
        Spans = spans;
    }

    public IReadOnlyList<TextSpan> Spans { get; }
}
