namespace Cycon.Render;

public interface IRenderBlock
{
    void Render(IRenderCanvas canvas, in BlockRenderContext ctx);
}

