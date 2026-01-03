using System;
using Cycon.Core.Scrolling;
using Cycon.Layout.Metrics;

namespace Cycon.Layout.Scrolling;

public static class ScrollbarLayouter
{
    public static ScrollbarLayout Layout(
        FixedCellGrid grid,
        int totalRows,
        int scrollOffsetRows,
        ScrollbarSettings settings)
    {
        if (settings.ThicknessPx <= 0)
        {
            return new ScrollbarLayout(false, default, default, default, default);
        }

        var viewportRows = grid.Rows;
        if (totalRows <= 0 || viewportRows <= 0 || totalRows <= viewportRows)
        {
            return new ScrollbarLayout(false, default, default, default, default);
        }

        var margin = Math.Max(0, settings.MarginPx);
        var thickness = Math.Max(0, settings.ThicknessPx);
        thickness = Math.Min(thickness, grid.FramebufferWidthPx);

        // Overlay scrollbar: do not reserve layout width; anchor to the framebuffer right edge.
        var trackX = grid.FramebufferWidthPx - thickness;
        var trackY = margin;
        var trackH = grid.FramebufferHeightPx - (margin * 2);
        if (trackH <= 0)
        {
            return new ScrollbarLayout(false, default, default, default, default);
        }

        if (trackX < 0)
        {
            trackX = 0;
        }

        if (thickness <= 0)
        {
            return new ScrollbarLayout(false, default, default, default, default);
        }

        var cellH = grid.CellHeightPx;
        var contentHeightPx = checked(totalRows * cellH);
        var viewportHeightPx = checked(viewportRows * cellH);
        var maxScrollYPx = Math.Max(0, contentHeightPx - viewportHeightPx);
        var maxScrollOffsetRows = Math.Max(0, totalRows - viewportRows);
        var clampedScrollOffsetRows = Math.Clamp(scrollOffsetRows, 0, maxScrollOffsetRows);
        var scrollYPx = (int)Math.Clamp(clampedScrollOffsetRows * (long)cellH, 0, maxScrollYPx);

        var minThumb = Math.Max(1, settings.MinThumbPx);
        var thumbH = (int)Math.Clamp(
            (long)trackH * viewportHeightPx / Math.Max(1, contentHeightPx),
            minThumb,
            trackH);

        var thumbTravel = Math.Max(0, trackH - thumbH);
        var thumbOffsetY = maxScrollYPx <= 0 || thumbTravel <= 0
            ? 0
            : (int)((long)thumbTravel * scrollYPx / maxScrollYPx);

        var track = new PxRect(trackX, trackY, thickness, trackH);
        var thumb = new PxRect(trackX, trackY + thumbOffsetY, thickness, thumbH);
        var hitExpand = 6;
        var hitTrack = ExpandAndClamp(track, hitExpand, grid.FramebufferWidthPx, grid.FramebufferHeightPx);
        var hitThumb = ExpandAndClamp(thumb, hitExpand, grid.FramebufferWidthPx, grid.FramebufferHeightPx);
        return new ScrollbarLayout(true, track, thumb, hitTrack, hitThumb);
    }

    private static PxRect ExpandAndClamp(PxRect rect, int expandPx, int framebufferWidth, int framebufferHeight)
    {
        expandPx = Math.Max(0, expandPx);
        if (expandPx == 0)
        {
            return rect;
        }

        var x = rect.X - expandPx;
        var y = rect.Y - expandPx;
        var w = rect.Width + (expandPx * 2);
        var h = rect.Height + (expandPx * 2);

        if (x < 0)
        {
            w += x;
            x = 0;
        }

        if (y < 0)
        {
            h += y;
            y = 0;
        }

        w = Math.Clamp(w, 0, Math.Max(0, framebufferWidth - x));
        h = Math.Clamp(h, 0, Math.Max(0, framebufferHeight - y));
        return new PxRect(x, y, w, h);
    }
}
