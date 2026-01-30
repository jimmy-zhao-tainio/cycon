using System;
using Cycon.Core.Transcript;
using Cycon.Render;

namespace Cycon.Rendering.Renderer;

internal static class BlockChromeRenderer
{
    public static void DrawChrome(RenderCanvas canvas, BlockChromeSpec chrome, RectPx rect, int borderRgba)
    {
        if (canvas is null) throw new ArgumentNullException(nameof(canvas));

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
                var frameRect = GetFrameRect(chrome, rect, out var thickness);
                DrawFrame(canvas, frameRect, thickness, borderRgba);
                break;
            }
        }
    }

    public static RectPx GetFrameRect(BlockChromeSpec chrome, RectPx rect, out int thickness)
    {
        thickness = Math.Max(1, chrome.BorderPx);
        var reservation = Math.Max(0, chrome.PaddingPx + chrome.BorderPx);
        var inset = Math.Max(0, (reservation - thickness) / 2);
        return inset > 0 ? DeflateRect(rect, inset) : rect;
    }

    public static void DrawFrame(RenderCanvas canvas, RectPx rect, int thickness, int rgba)
    {
        if (canvas is null) throw new ArgumentNullException(nameof(canvas));

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

    public static RectPx DeflateRect(RectPx rect, int inset)
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

    public static RectPx SnapToCellGrid(RectPx rect, int cellW, int cellH)
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
