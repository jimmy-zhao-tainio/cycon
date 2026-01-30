using System;
using Cycon.Render;

namespace Cycon.Rendering.Renderer;

internal static class RoundedRectRenderer
{
    public static void FillRoundedRect(RenderCanvas canvas, RectPx rect, int radiusPx, int rgba)
    {
        if (canvas is null) throw new ArgumentNullException(nameof(canvas));

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var maxRadius = Math.Min(rect.Width / 2, rect.Height / 2);
        radiusPx = Math.Clamp(radiusPx, 0, maxRadius);
        if (radiusPx <= 0)
        {
            canvas.FillRect(rect, rgba);
            return;
        }

        var innerW = rect.Width - (radiusPx * 2);
        var innerH = rect.Height - (radiusPx * 2);

        // Center strip.
        if (innerW > 0)
        {
            canvas.FillRect(new RectPx(rect.X + radiusPx, rect.Y, innerW, rect.Height), rgba);
        }

        // Side strips (between rounded corners).
        if (innerH > 0)
        {
            canvas.FillRect(new RectPx(rect.X, rect.Y + radiusPx, radiusPx, innerH), rgba);
            canvas.FillRect(new RectPx(rect.X + rect.Width - radiusPx, rect.Y + radiusPx, radiusPx, innerH), rgba);
        }

        FillCorner(canvas, rect.X, rect.Y, radiusPx, rgba, Corner.TopLeft);
        FillCorner(canvas, rect.X + rect.Width - radiusPx, rect.Y, radiusPx, rgba, Corner.TopRight);
        FillCorner(canvas, rect.X, rect.Y + rect.Height - radiusPx, radiusPx, rgba, Corner.BottomLeft);
        FillCorner(canvas, rect.X + rect.Width - radiusPx, rect.Y + rect.Height - radiusPx, radiusPx, rgba, Corner.BottomRight);
    }

    public static void DrawRoundedFrame(RenderCanvas canvas, RectPx rect, int thickness, int radiusPx, int rgba)
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

        var maxRadius = Math.Min(rect.Width / 2, rect.Height / 2);
        radiusPx = Math.Clamp(radiusPx, 0, maxRadius);
        if (radiusPx <= 0 || radiusPx <= thickness)
        {
            BlockChromeRenderer.DrawFrame(canvas, rect, thickness, rgba);
            return;
        }

        // Edge strips (avoid corner boxes).
        var innerW = rect.Width - (radiusPx * 2);
        var innerH = rect.Height - (radiusPx * 2);
        if (innerW > 0)
        {
            canvas.FillRect(new RectPx(rect.X + radiusPx, rect.Y, innerW, thickness), rgba);
            canvas.FillRect(new RectPx(rect.X + radiusPx, rect.Y + rect.Height - thickness, innerW, thickness), rgba);
        }

        if (innerH > 0)
        {
            canvas.FillRect(new RectPx(rect.X, rect.Y + radiusPx, thickness, innerH), rgba);
            canvas.FillRect(new RectPx(rect.X + rect.Width - thickness, rect.Y + radiusPx, thickness, innerH), rgba);
        }

        DrawCorner(canvas, rect.X, rect.Y, thickness, radiusPx, rgba, Corner.TopLeft);
        DrawCorner(canvas, rect.X + rect.Width - radiusPx, rect.Y, thickness, radiusPx, rgba, Corner.TopRight);
        DrawCorner(canvas, rect.X, rect.Y + rect.Height - radiusPx, thickness, radiusPx, rgba, Corner.BottomLeft);
        DrawCorner(canvas, rect.X + rect.Width - radiusPx, rect.Y + rect.Height - radiusPx, thickness, radiusPx, rgba, Corner.BottomRight);
    }

    private enum Corner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private static void FillCorner(RenderCanvas canvas, int originX, int originY, int radiusPx, int rgba, Corner corner)
    {
        var r = radiusPx;
        if (r <= 0)
        {
            return;
        }

        var outer2 = r * r;
        for (var y = 0; y < r; y++)
        {
            var rowStart = -1;
            var rowEnd = -1;

            for (var x = 0; x < r; x++)
            {
                var dx = (x + 0.5) - r;
                var dy = (y + 0.5) - r;
                var d2 = (dx * dx) + (dy * dy);
                if (d2 <= outer2)
                {
                    if (rowStart < 0) rowStart = x;
                    rowEnd = x;
                }
                else if (rowStart >= 0)
                {
                    break;
                }
            }

            if (rowStart < 0)
            {
                continue;
            }

            var w = (rowEnd - rowStart) + 1;
            var px = originX + rowStart;
            var py = originY + y;

            switch (corner)
            {
                case Corner.TopLeft:
                    break;
                case Corner.TopRight:
                    px = originX + (r - rowStart - w);
                    break;
                case Corner.BottomLeft:
                    py = originY + (r - y - 1);
                    break;
                case Corner.BottomRight:
                    px = originX + (r - rowStart - w);
                    py = originY + (r - y - 1);
                    break;
            }

            canvas.FillRect(new RectPx(px, py, w, 1), rgba);
        }
    }

    private static void DrawCorner(RenderCanvas canvas, int originX, int originY, int thickness, int radiusPx, int rgba, Corner corner)
    {
        // Border ring between outer circle radius and inner circle (radius - thickness).
        var rOuter = radiusPx;
        var rInner = radiusPx - thickness;
        if (rInner <= 0)
        {
            return;
        }

        var outer2 = rOuter * rOuter;
        var inner2 = rInner * rInner;

        for (var y = 0; y < radiusPx; y++)
        {
            var rowStart = -1;
            var rowEnd = -1;

            for (var x = 0; x < radiusPx; x++)
            {
                // Pixel center distance from arc center at (radiusPx, radiusPx) within this corner box.
                var dx = (x + 0.5) - rOuter;
                var dy = (y + 0.5) - rOuter;
                var d2 = (dx * dx) + (dy * dy);

                if (d2 <= outer2 && d2 >= inner2)
                {
                    if (rowStart < 0) rowStart = x;
                    rowEnd = x;
                }
                else if (rowStart >= 0)
                {
                    break;
                }
            }

            if (rowStart < 0)
            {
                continue;
            }

            var w = (rowEnd - rowStart) + 1;
            var px = originX + rowStart;
            var py = originY + y;

            switch (corner)
            {
                case Corner.TopLeft:
                    // As-is.
                    break;
                case Corner.TopRight:
                    px = originX + (radiusPx - rowStart - w);
                    break;
                case Corner.BottomLeft:
                    py = originY + (radiusPx - y - 1);
                    break;
                case Corner.BottomRight:
                    px = originX + (radiusPx - rowStart - w);
                    py = originY + (radiusPx - y - 1);
                    break;
            }

            canvas.FillRect(new RectPx(px, py, w, 1), rgba);
        }
    }
}
