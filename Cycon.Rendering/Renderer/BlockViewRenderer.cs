using System;
using Cycon.Core.Fonts;
using Cycon.Core.Transcript;
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

        var outerRect = ctx.ViewportRectPx;
        var chrome = block is IBlockChromeProvider chromeProvider
            ? chromeProvider.ChromeSpec
            : BlockChromeSpec.Disabled;

        if (chrome.Enabled)
        {
            DrawChrome(canvas, chrome, outerRect, ctx.Theme.ForegroundRgba);
        }

        var innerRect = chrome.Enabled
            ? DeflateRect(outerRect, Math.Max(0, chrome.BorderPx + chrome.PaddingPx))
            : outerRect;

        innerRect = SnapToCellGrid(innerRect, ctx.TextMetrics.CellWidthPx, ctx.TextMetrics.CellHeightPx);

        var innerCtx = new BlockRenderContext(innerRect, ctx.TimeSeconds, ctx.Theme, ctx.TextMetrics, ctx.Scene3D);
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

    private static void DrawChrome(RenderCanvas canvas, BlockChromeSpec chrome, RectPx rect, int borderRgba)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        switch (chrome.Style)
        {
            case BlockChromeStyle.PanelBg:
                canvas.FillRect(rect, borderRgba);
                break;
            case BlockChromeStyle.Frame2Px:
            {
                var thickness = Math.Max(1, chrome.BorderPx);
                var reservation = Math.Max(0, chrome.PaddingPx + chrome.BorderPx);
                var inset = Math.Max(0, (reservation - thickness) / 2);
                var frameRect = inset > 0 ? DeflateRect(rect, inset) : rect;
                DrawFrame(canvas, frameRect, thickness, borderRgba);
                break;
            }
        }
    }

    private static void DrawFrame(RenderCanvas canvas, RectPx rect, int thickness, int rgba)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var maxThickness = Math.Min(rect.Width / 2, rect.Height / 2);
        thickness = Math.Max(1, Math.Min(thickness, maxThickness));

        if (thickness <= 0)
        {
            return;
        }

        canvas.FillRect(new RectPx(rect.X, rect.Y, rect.Width, thickness), rgba);
        canvas.FillRect(new RectPx(rect.X, rect.Y + rect.Height - thickness, rect.Width, thickness), rgba);
        canvas.FillRect(new RectPx(rect.X, rect.Y + thickness, thickness, rect.Height - (thickness * 2)), rgba);
        canvas.FillRect(new RectPx(rect.X + rect.Width - thickness, rect.Y + thickness, thickness, rect.Height - (thickness * 2)), rgba);
    }

    private static RectPx DeflateRect(RectPx rect, int inset)
    {
        if (inset <= 0)
        {
            return rect;
        }

        var x = rect.X + inset;
        var y = rect.Y + inset;
        var w = Math.Max(0, rect.Width - (inset * 2));
        var h = Math.Max(0, rect.Height - (inset * 2));
        return new RectPx(x, y, w, h);
    }

    private static RectPx SnapToCellGrid(RectPx rect, int cellW, int cellH)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return rect;
        }

        cellW = Math.Max(1, cellW);
        cellH = Math.Max(1, cellH);

        var snappedW = rect.Width >= cellW ? rect.Width - (rect.Width % cellW) : rect.Width;
        var snappedH = rect.Height >= cellH ? rect.Height - (rect.Height % cellH) : rect.Height;
        return new RectPx(rect.X, rect.Y, snappedW, snappedH);
    }
}
