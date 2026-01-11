namespace Cycon.Render;

public interface IBlockOverlayRenderer
{
    void RenderOverlay(IRenderCanvas canvas, RectPx outerViewportRectPx, in BlockRenderContext ctx);
}
