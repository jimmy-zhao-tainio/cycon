using System;
using Cycon.Core.Fonts;
using Cycon.Render;
using Cycon.Rendering.Commands;

namespace Cycon.Rendering.Renderer;

public static class BlockViewRenderer
{
    public static void RenderFullscreen(
        RenderFrame frame,
        IConsoleFont font,
        IRenderBlock block,
        in BlockRenderContext ctx,
        int framebufferWidth,
        int framebufferHeight)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (font is null) throw new ArgumentNullException(nameof(font));
        if (block is null) throw new ArgumentNullException(nameof(block));

        if (framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            return;
        }

        var canvas = new RenderCanvas(frame, font);

        canvas.SetDebugTag(block is Cycon.Core.Transcript.IBlock b ? b.Id.Value : 0);

        // Ensure the border/outside area is cleared deterministically.
        canvas.PushClipRect(new RectPx(0, 0, framebufferWidth, framebufferHeight));
        canvas.FillRect(new RectPx(0, 0, framebufferWidth, framebufferHeight), ctx.Theme.BackgroundRgba);

        canvas.PushClipRect(ctx.ViewportRectPx);
        block.Render(canvas, ctx);
        canvas.PopClipRect();

        canvas.PopClipRect();
        canvas.SetDebugTag(0);

        // Restore default 2D state for safety (the block may have configured depth/culling).
        frame.Add(new SetColorWrite(true));
        frame.Add(new SetDepthState(false, false, DepthFuncKind.Less));
        frame.Add(new SetCullState(false, true));
    }
}
