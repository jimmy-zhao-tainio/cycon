using Cycon.Backends.Abstractions.Rendering;

namespace Cycon.Backends.Abstractions.Tests.Rendering;

public sealed class DrawCommandBatcherTests
{
    [Fact]
    public void ComputeBatches_ProducesQuadThenGlyph()
    {
        var commands = new DrawCommand[]
        {
            new DrawQuad(0, 0, 8, 16, unchecked((int)0xEEEEEEFF)),
            new DrawGlyphRun(0, 0, new[] { new GlyphInstance('A', 0, 0, unchecked((int)0x000000FF)) })
        };

        var batches = DrawCommandBatcher.ComputeBatches(commands);

        Assert.Equal(2, batches.Count);
        Assert.Equal(new DrawCommandBatch(DrawCommandBatchKind.Quad, StartIndex: 0, Count: 1), batches[0]);
        Assert.Equal(new DrawCommandBatch(DrawCommandBatchKind.Glyph, StartIndex: 1, Count: 1), batches[1]);
    }
}

