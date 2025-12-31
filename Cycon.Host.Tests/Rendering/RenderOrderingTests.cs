using BackendRender = Cycon.Backends.Abstractions.Rendering;
using Cycon.Host.Services;
using RenderingCommands = Cycon.Rendering.Commands;

namespace Cycon.Host.Tests.Rendering;

public sealed class RenderOrderingTests
{
    [Fact]
    public void Adaptation_PreservesQuadThenGlyphOrder()
    {
        var frame = new Cycon.Rendering.RenderFrame();
        frame.Add(new RenderingCommands.DrawQuad(0, 0, 8, 16, unchecked((int)0xEEEEEEFF)));
        frame.Add(new RenderingCommands.DrawGlyphRun(
            0,
            0,
            new[] { new RenderingCommands.GlyphInstance('A', 0, 0, unchecked((int)0x000000FF)) }));

        var adapted = RenderFrameAdapter.Adapt(frame);

        Assert.Equal(2, adapted.Commands.Count);
        Assert.IsType<BackendRender.DrawQuad>(adapted.Commands[0]);
        Assert.IsType<BackendRender.DrawGlyphRun>(adapted.Commands[1]);
    }
}
