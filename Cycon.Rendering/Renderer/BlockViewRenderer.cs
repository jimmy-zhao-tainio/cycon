using System;
using Cycon.Core.Fonts;
using Cycon.Core.Transcript;
using Cycon.Render;
using Cycon.Rendering.Inspect;
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

        var outerRect = ctx.ViewportRectPx;
        var chrome = block is IBlockChromeProvider chromeProvider
            ? chromeProvider.ChromeSpec
            : BlockChromeSpec.Disabled;

        if (chrome.Enabled)
        {
            BlockChromeRenderer.DrawChrome(canvas, chrome, outerRect, ctx.Theme.ForegroundRgba);
        }

        var innerRect = chrome.Enabled
            ? BlockChromeRenderer.DeflateRect(outerRect, Math.Max(0, chrome.BorderPx + chrome.PaddingPx))
            : outerRect;

        innerRect = BlockChromeRenderer.SnapToCellGrid(innerRect, ctx.TextMetrics.CellWidthPx, ctx.TextMetrics.CellHeightPx);

        var innerCtx = new BlockRenderContext(
            innerRect,
            ctx.TimeSeconds,
            ctx.Theme,
            ctx.TextMetrics,
            ctx.Scene3D,
            framebufferWidth,
            framebufferHeight);
        canvas.PushClipRect(innerRect);
        block.Render(canvas, innerCtx);
        canvas.PopClipRect();

        if (block is IBlockOverlayRenderer overlayRenderer)
        {
            overlayRenderer.RenderOverlay(canvas, outerRect, innerCtx);
        }

        canvas.PopClipRect();
        canvas.SetDebugTag(0);

        // Restore default 2D state for safety (the block may have configured depth/culling).
        frame.Add(new SetColorWrite(true));
        frame.Add(new SetDepthState(false, false, DepthFuncKind.Less));
        frame.Add(new SetCullState(false, true));
    }

    public static void RenderFullscreenInspect(
        RenderFrame frame,
        IConsoleFont font,
        IRenderBlock block,
        in BlockRenderContext ctx,
        int framebufferWidth,
        int framebufferHeight,
        in InspectLayoutResult layout,
        in InspectChromeSpec inspectChromeSpec,
        ref InspectChromeDataBuilder inspectChromeData)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        if (font is null) throw new ArgumentNullException(nameof(font));
        if (block is null) throw new ArgumentNullException(nameof(block));

        if (framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            return;
        }

        if (layout.ContentRect.Width <= 0 || layout.ContentRect.Height <= 0)
        {
            return;
        }

        var canvas = new RenderCanvas(frame, font);

        canvas.SetDebugTag(block is Cycon.Core.Transcript.IBlock b ? b.Id.Value : 0);

        canvas.PushClipRect(new RectPx(0, 0, framebufferWidth, framebufferHeight));
        canvas.FillRect(new RectPx(0, 0, framebufferWidth, framebufferHeight), ctx.Theme.BackgroundRgba);

        var outerRect = ctx.ViewportRectPx;
        var chrome = block is IBlockChromeProvider chromeProvider
            ? chromeProvider.ChromeSpec
            : BlockChromeSpec.Disabled;

        if (chrome.Enabled)
        {
            BlockChromeRenderer.DrawChrome(canvas, chrome, outerRect, ctx.Theme.ForegroundRgba);
        }

        InspectChromeRenderer.Draw(canvas, layout, inspectChromeSpec, ref inspectChromeData, ctx.Theme, ctx.TextMetrics);

        var contentCtx = new BlockRenderContext(
            layout.ContentRect,
            ctx.TimeSeconds,
            ctx.Theme,
            ctx.TextMetrics,
            ctx.Scene3D,
            framebufferWidth,
            framebufferHeight);
        canvas.PushClipRect(layout.ContentRect);
        block.Render(canvas, contentCtx);
        canvas.PopClipRect();

        if (block is IBlockOverlayRenderer overlayRenderer)
        {
            overlayRenderer.RenderOverlay(canvas, outerRect, contentCtx);
        }

        canvas.PopClipRect();
        canvas.SetDebugTag(0);

        frame.Add(new SetColorWrite(true));
        frame.Add(new SetDepthState(false, false, DepthFuncKind.Less));
        frame.Add(new SetCullState(false, true));
    }

}
