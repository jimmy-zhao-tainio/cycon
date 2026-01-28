using System;
using Cycon.Core;
using Cycon.Core.Scrolling;
using Cycon.Core.Settings;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;

namespace Cycon.Host.Scrolling;

internal sealed class ConsoleScrollModel : IScrollModel
{
    private readonly ConsoleDocument _document;
    private readonly LayoutSettings _layoutSettings;

    public ConsoleScrollModel(ConsoleDocument document, LayoutSettings layoutSettings)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _layoutSettings = layoutSettings;
    }

    public int TotalRows { get; set; }

    public bool TryGetScrollbarLayout(PxRect viewportRectPx, ScrollbarSettings settings, out ScrollbarLayout layout)
    {
        layout = default;

        if (TotalRows <= 0 || viewportRectPx.Width <= 0 || viewportRectPx.Height <= 0)
        {
            return false;
        }

        var grid = FixedCellGrid.FromViewport(new ConsoleViewport(viewportRectPx.Width, viewportRectPx.Height), _layoutSettings);
        var maxScrollOffsetRows = grid.Rows <= 0 ? 0 : Math.Max(0, TotalRows - grid.Rows);
        var cellH = grid.CellHeightPx;
        var clampedScrollOffsetRows = cellH <= 0
            ? 0
            : Math.Clamp(_document.Scroll.ScrollOffsetPx / cellH, 0, maxScrollOffsetRows);
        var sb = ScrollbarLayouter.Layout(grid, TotalRows, clampedScrollOffsetRows, settings);
        if (!sb.IsScrollable)
        {
            layout = sb;
            return true;
        }

        layout = Offset(sb, viewportRectPx.X, viewportRectPx.Y);
        return true;
    }

    public bool ApplyWheelDelta(int wheelDelta, PxRect viewportRectPx)
    {
        if (wheelDelta == 0 || TotalRows <= 0 || viewportRectPx.Width <= 0 || viewportRectPx.Height <= 0)
        {
            return false;
        }

        var grid = FixedCellGrid.FromViewport(new ConsoleViewport(viewportRectPx.Width, viewportRectPx.Height), _layoutSettings);
        var maxScrollOffsetRows = grid.Rows <= 0 ? 0 : Math.Max(0, TotalRows - grid.Rows);
        if (maxScrollOffsetRows == 0)
        {
            return false;
        }

        var cellH = grid.CellHeightPx;
        var maxScrollOffsetPx = Math.Max(0, maxScrollOffsetRows * cellH);

        var before = _document.Scroll.ScrollOffsetPx;
        var deltaPx = -wheelDelta;
        _document.Scroll.ApplyUserScrollDelta(deltaPx, maxScrollOffsetPx);
        return _document.Scroll.ScrollOffsetPx != before;
    }

    public bool DragThumbTo(int pointerYPx, int grabOffsetYPx, PxRect viewportRectPx, ScrollbarLayout layout)
    {
        if (TotalRows <= 0 || viewportRectPx.Width <= 0 || viewportRectPx.Height <= 0)
        {
            return false;
        }

        var grid = FixedCellGrid.FromViewport(new ConsoleViewport(viewportRectPx.Width, viewportRectPx.Height), _layoutSettings);
        var cellH = grid.CellHeightPx;
        if (cellH <= 0)
        {
            return false;
        }

        var track = layout.TrackRectPx;
        var thumb = layout.ThumbRectPx;

        var desiredThumbTop = pointerYPx - grabOffsetYPx;
        var maxThumbTop = track.Y + Math.Max(0, track.Height - thumb.Height);
        desiredThumbTop = Math.Clamp(desiredThumbTop, track.Y, maxThumbTop);

        var contentHeightPx = TotalRows * cellH;
        var viewportHeightPx = grid.Rows * cellH;
        var maxScrollYPx = Math.Max(0, contentHeightPx - viewportHeightPx);
        var thumbTravel = Math.Max(0, track.Height - thumb.Height);

        var newScrollYPx = (thumbTravel <= 0 || maxScrollYPx <= 0)
            ? 0
            : (int)((long)(desiredThumbTop - track.Y) * maxScrollYPx / thumbTravel);

        var before = _document.Scroll.ScrollOffsetPx;
        SetScrollOffsetPx(newScrollYPx, maxScrollYPx);
        return _document.Scroll.ScrollOffsetPx != before;
    }

    private void SetScrollOffsetPx(int scrollOffsetPx, int maxScrollOffsetPx)
    {
        var clamped = Math.Clamp(scrollOffsetPx, 0, maxScrollOffsetPx);
        _document.Scroll.ScrollOffsetPx = clamped;
        _document.Scroll.IsFollowingTail = clamped >= maxScrollOffsetPx;
        _document.Scroll.ScrollPxFromBottom = maxScrollOffsetPx - clamped;
    }

    private static ScrollbarLayout Offset(ScrollbarLayout sb, int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return sb;
        }

        static PxRect Add(PxRect r, int x, int y) => new(r.X + x, r.Y + y, r.Width, r.Height);

        return new ScrollbarLayout(
            sb.IsScrollable,
            Add(sb.TrackRectPx, dx, dy),
            Add(sb.ThumbRectPx, dx, dy),
            Add(sb.HitTrackRectPx, dx, dy),
            Add(sb.HitThumbRectPx, dx, dy));
    }
}
