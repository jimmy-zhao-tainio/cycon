using System;
using System.Collections.Generic;
using Cycon.Host.Scrolling;
using Cycon.Layout.Scrolling;
using Cycon.Core.Scrolling;

namespace Extensions.Inspect.Blocks;

public sealed class TextScrollModel : IScrollModel
{
    private const int BlockSize = 1024;

    private readonly IReadOnlyList<string> _lines;
    private readonly Dictionary<long, WrappedLine> _wrapCache = new();
    private readonly Dictionary<int, int[]> _blockSumsByCols = new();
    private int _cellWidthPx = 8;
    private int _cellHeightPx = 16;

    private int _wrapCols = 1;
    private int _viewportRows = 1;
    private int _rightPaddingPx;
    private int _insetLeftPx;
    private int _insetTopPx;
    private int _insetRightPx;
    private int _insetBottomPx;
    private int _scrollbarChromeInsetPx;

    private int _topLineIndex;
    private int _topLineSubRow;
    private int _scrollOffsetRows;

    public TextScrollModel(IReadOnlyList<string> lines)
    {
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
    }

    public void UpdateTextMetrics(int cellWidthPx, int cellHeightPx)
    {
        if (cellWidthPx > 0)
        {
            _cellWidthPx = cellWidthPx;
        }

        if (cellHeightPx > 0)
        {
            _cellHeightPx = cellHeightPx;
        }
    }

    public void SetRightPaddingPx(int rightPaddingPx)
    {
        _rightPaddingPx = Math.Max(0, rightPaddingPx);
    }

    public void SetContentInsetsPx(int leftPx, int topPx, int rightPx, int bottomPx)
    {
        _insetLeftPx = Math.Max(0, leftPx);
        _insetTopPx = Math.Max(0, topPx);
        _insetRightPx = Math.Max(0, rightPx);
        _insetBottomPx = Math.Max(0, bottomPx);
    }

    public void SetScrollbarChromeInsetPx(int insetPx)
    {
        _scrollbarChromeInsetPx = Math.Max(0, insetPx);
    }

    public void UpdateViewport(PxRect viewportRectPx)
    {
        var availableWidth = Math.Max(0, viewportRectPx.Width - _insetLeftPx - _insetRightPx - _rightPaddingPx);
        var availableHeight = Math.Max(0, viewportRectPx.Height - _insetTopPx - _insetBottomPx);
        var nextCols = Math.Max(1, availableWidth / Math.Max(1, _cellWidthPx));
        var nextRows = Math.Max(1, availableHeight / Math.Max(1, _cellHeightPx));

        if (nextCols != _wrapCols)
        {
            _wrapCols = nextCols;
            _topLineSubRow = 0;
            _scrollOffsetRows = ComputeScrollOffsetRowsFromAnchor(_topLineIndex, _topLineSubRow, _wrapCols);
        }

        _viewportRows = nextRows;
        ClampToValidRange(viewportRectPx);
    }

    public bool TryGetScrollbarLayout(PxRect viewportRectPx, ScrollbarSettings settings, out ScrollbarLayout layout)
    {
        layout = default;

        if (_lines.Count == 0 || viewportRectPx.Width <= 0 || viewportRectPx.Height <= 0)
        {
            return false;
        }

        UpdateViewport(viewportRectPx);

        var totalRows = GetEstimatedTotalRows(_wrapCols);
        var scrollbarTrackH = viewportRectPx.Height + (_scrollbarChromeInsetPx * 2);
        var scrollbarViewportRows = Math.Max(1, scrollbarTrackH / Math.Max(1, _cellHeightPx));
        if (totalRows <= scrollbarViewportRows || scrollbarViewportRows <= 0)
        {
            layout = new ScrollbarLayout(false, default, default, default, default);
            return true;
        }

        var thickness = Math.Max(0, settings.ThicknessPx);
        thickness = Math.Min(thickness, viewportRectPx.Width);
        if (thickness <= 0)
        {
            layout = new ScrollbarLayout(false, default, default, default, default);
            return true;
        }

        var trackX = viewportRectPx.X + (viewportRectPx.Width - thickness) + _scrollbarChromeInsetPx;
        var trackY = viewportRectPx.Y - _scrollbarChromeInsetPx;
        var trackH = scrollbarTrackH;
        if (trackH <= 0)
        {
            layout = new ScrollbarLayout(false, default, default, default, default);
            return true;
        }

        var maxScrollOffsetRows = Math.Max(0, totalRows - scrollbarViewportRows);
        var clampedScrollOffsetRows = Math.Clamp(_scrollOffsetRows, 0, maxScrollOffsetRows);
        _scrollOffsetRows = clampedScrollOffsetRows;

        var contentHeightPx = checked(totalRows * _cellHeightPx);
        var viewportHeightPx = checked(scrollbarViewportRows * _cellHeightPx);
        var maxScrollYPx = Math.Max(0, contentHeightPx - viewportHeightPx);
        var scrollYPx = (int)Math.Clamp(clampedScrollOffsetRows * (long)_cellHeightPx, 0, maxScrollYPx);

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
        var hitTrack = ExpandAndClamp(track, hitExpand, viewportRectPx);
        var hitThumb = ExpandAndClamp(thumb, hitExpand, viewportRectPx);
        layout = new ScrollbarLayout(true, track, thumb, hitTrack, hitThumb);
        return true;
    }

    public bool ApplyWheelDelta(int wheelDelta, PxRect viewportRectPx)
    {
        if (wheelDelta == 0 || _lines.Count == 0)
        {
            return false;
        }

        UpdateViewport(viewportRectPx);
        var before = _scrollOffsetRows;
        // Wheel deltas are pixel-scaled (48px per wheel unit) to support smooth scrolling.
        // Normalize back to wheel "units" for row-based scrolling.
        var wheelUnits = wheelDelta / 48f;
        var deltaRows = (int)MathF.Round(-wheelUnits * 3f);
        if (deltaRows == 0)
        {
            deltaRows = wheelDelta < 0 ? 1 : -1;
        }
        ScrollByRowsInternal(deltaRows, viewportRectPx);
        return _scrollOffsetRows != before;
    }

    public bool ScrollByRows(int deltaRows, PxRect viewportRectPx)
    {
        if (deltaRows == 0 || _lines.Count == 0)
        {
            return false;
        }

        UpdateViewport(viewportRectPx);
        var before = _scrollOffsetRows;
        ScrollByRowsInternal(deltaRows, viewportRectPx);
        return _scrollOffsetRows != before;
    }

    public bool DragThumbTo(int pointerYPx, int grabOffsetYPx, PxRect viewportRectPx, ScrollbarLayout layout)
    {
        if (_lines.Count == 0)
        {
            return false;
        }

        UpdateViewport(viewportRectPx);

        var track = layout.TrackRectPx;
        var thumb = layout.ThumbRectPx;

        var totalRows = GetEstimatedTotalRows(_wrapCols);
        var viewportRows = _viewportRows;
        var maxScrollOffsetRows = Math.Max(0, totalRows - viewportRows);
        if (maxScrollOffsetRows == 0)
        {
            return false;
        }

        var before = _scrollOffsetRows;

        var desiredThumbTop = pointerYPx - grabOffsetYPx;
        var maxThumbTop = track.Y + Math.Max(0, track.Height - thumb.Height);
        desiredThumbTop = Math.Clamp(desiredThumbTop, track.Y, maxThumbTop);

        var contentHeightPx = checked(totalRows * _cellHeightPx);
        var viewportHeightPx = checked(viewportRows * _cellHeightPx);
        var maxScrollYPx = Math.Max(0, contentHeightPx - viewportHeightPx);
        var thumbTravel = Math.Max(0, track.Height - thumb.Height);

        var newScrollYPx = (thumbTravel <= 0 || maxScrollYPx <= 0)
            ? 0
            : (int)((long)(desiredThumbTop - track.Y) * maxScrollYPx / thumbTravel);

        var newScrollRows = (newScrollYPx + (_cellHeightPx / 2)) / _cellHeightPx;
        SetScrollOffsetRows(newScrollRows, viewportRectPx);
        return _scrollOffsetRows != before;
    }

    public int GetWrapColsForRender() => _wrapCols;

    public int GetViewportRowsForRender() => _viewportRows;

    public int ComputeWrappedRowsCapped(int wrapCols, int capRows)
    {
        wrapCols = Math.Max(1, wrapCols);
        capRows = Math.Max(0, capRows);

        var total = 0;
        for (var i = 0; i < _lines.Count; i++)
        {
            var wrapped = GetOrComputeWrappedLine(i, wrapCols);
            total = checked(total + wrapped.RowCount);
            if (capRows > 0 && total >= capRows)
            {
                return total;
            }
        }

        return total;
    }

    public WrappedLine GetWrappedLine(int lineIndex)
    {
        lineIndex = Math.Clamp(lineIndex, 0, Math.Max(0, _lines.Count - 1));
        return GetOrComputeWrappedLine(lineIndex, _wrapCols);
    }

    public int TopLineIndex => _topLineIndex;

    public int TopLineSubRow => _topLineSubRow;

    public bool EnsureAnchorVisible(int lineIndex, int subRow, PxRect viewportRectPx)
    {
        if (_lines.Count == 0)
        {
            return false;
        }

        UpdateViewport(viewportRectPx);

        lineIndex = Math.Clamp(lineIndex, 0, _lines.Count - 1);
        var wrapped = GetOrComputeWrappedLine(lineIndex, _wrapCols);
        subRow = Math.Clamp(subRow, 0, Math.Max(0, wrapped.RowCount - 1));

        var caretRow = ComputeScrollOffsetRowsFromAnchor(lineIndex, subRow, _wrapCols);
        var topRow = _scrollOffsetRows;
        var bottomRowExclusive = topRow + _viewportRows;

        if (caretRow < topRow)
        {
            SetScrollOffsetRows(caretRow, viewportRectPx);
            return true;
        }

        if (caretRow >= bottomRowExclusive)
        {
            var nextTop = Math.Max(0, caretRow - Math.Max(0, _viewportRows - 1));
            SetScrollOffsetRows(nextTop, viewportRectPx);
            return true;
        }

        return false;
    }

    private void ScrollByRowsInternal(int deltaRows, PxRect viewportRectPx)
    {
        var totalRows = GetEstimatedTotalRows(_wrapCols);
        var maxScrollOffsetRows = Math.Max(0, totalRows - _viewportRows);
        var next = Math.Clamp(_scrollOffsetRows + deltaRows, 0, maxScrollOffsetRows);
        _scrollOffsetRows = next;
        UpdateAnchorFromScrollOffset(next);
        ClampToValidRange(viewportRectPx);
    }

    private void SetScrollOffsetRows(int scrollOffsetRows, PxRect viewportRectPx)
    {
        var totalRows = GetEstimatedTotalRows(_wrapCols);
        var maxScrollOffsetRows = Math.Max(0, totalRows - _viewportRows);
        var next = Math.Clamp(scrollOffsetRows, 0, maxScrollOffsetRows);
        _scrollOffsetRows = next;
        UpdateAnchorFromScrollOffset(next);
        ClampToValidRange(viewportRectPx);
    }

    private void ClampToValidRange(PxRect viewportRectPx)
    {
        if (_lines.Count == 0)
        {
            _topLineIndex = 0;
            _topLineSubRow = 0;
            _scrollOffsetRows = 0;
            return;
        }

        _topLineIndex = Math.Clamp(_topLineIndex, 0, _lines.Count - 1);
        var wrapped = GetOrComputeWrappedLine(_topLineIndex, _wrapCols);
        _topLineSubRow = Math.Clamp(_topLineSubRow, 0, Math.Max(0, wrapped.RowCount - 1));

        var totalRows = GetEstimatedTotalRows(_wrapCols);
        var maxScrollOffsetRows = Math.Max(0, totalRows - _viewportRows);
        var clampedScrollOffsetRows = Math.Clamp(_scrollOffsetRows, 0, maxScrollOffsetRows);
        if (clampedScrollOffsetRows != _scrollOffsetRows)
        {
            _scrollOffsetRows = clampedScrollOffsetRows;
            UpdateAnchorFromScrollOffset(clampedScrollOffsetRows);
        }
    }

    private int GetEstimatedTotalRows(int wrapCols)
    {
        if (_lines.Count == 0)
        {
            return 0;
        }

        var sums = GetOrBuildBlockSums(wrapCols);
        var total = 0;
        for (var i = 0; i < sums.Length; i++)
        {
            total = checked(total + sums[i]);
        }
        return total;
    }

    private int[] GetOrBuildBlockSums(int wrapCols)
    {
        if (_blockSumsByCols.TryGetValue(wrapCols, out var sums))
        {
            return sums;
        }

        var blocks = (_lines.Count + BlockSize - 1) / BlockSize;
        sums = new int[blocks];

        var lineIndex = 0;
        for (var block = 0; block < blocks; block++)
        {
            var sum = 0;
            var end = Math.Min(_lines.Count, lineIndex + BlockSize);
            for (; lineIndex < end; lineIndex++)
            {
                sum = checked(sum + EstimateLineRows(_lines[lineIndex], wrapCols));
            }

            sums[block] = sum;
        }

        _blockSumsByCols[wrapCols] = sums;
        return sums;
    }

    private int ComputeScrollOffsetRowsFromAnchor(int topLineIndex, int topLineSubRow, int wrapCols)
    {
        if (topLineIndex <= 0)
        {
            return Math.Max(0, topLineSubRow);
        }

        var sums = GetOrBuildBlockSums(wrapCols);
        var blockCount = sums.Length;
        var blockIndex = topLineIndex / BlockSize;
        blockIndex = Math.Clamp(blockIndex, 0, Math.Max(0, blockCount - 1));

        var rows = 0;
        for (var i = 0; i < blockIndex; i++)
        {
            rows = checked(rows + sums[i]);
        }

        var startLine = blockIndex * BlockSize;
        for (var i = startLine; i < topLineIndex; i++)
        {
            rows = checked(rows + EstimateLineRows(_lines[i], wrapCols));
        }

        rows = checked(rows + Math.Max(0, topLineSubRow));
        return rows;
    }

    private void UpdateAnchorFromScrollOffset(int scrollOffsetRows)
    {
        var sums = GetOrBuildBlockSums(_wrapCols);
        var remaining = scrollOffsetRows;
        var blockIndex = 0;
        while (blockIndex < sums.Length && remaining >= sums[blockIndex])
        {
            remaining -= sums[blockIndex];
            blockIndex++;
        }

        var lineIndex = blockIndex * BlockSize;
        while (lineIndex < _lines.Count)
        {
            var estimate = EstimateLineRows(_lines[lineIndex], _wrapCols);
            if (remaining < estimate)
            {
                _topLineIndex = lineIndex;
                _topLineSubRow = remaining;
                return;
            }

            remaining -= estimate;
            lineIndex++;
        }

        _topLineIndex = Math.Max(0, _lines.Count - 1);
        _topLineSubRow = 0;
    }

    private WrappedLine GetOrComputeWrappedLine(int lineIndex, int wrapCols)
    {
        var key = ((long)wrapCols << 32) | (uint)lineIndex;
        if (_wrapCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var line = _lines[lineIndex];
        var wrapped = WrapLine(line, wrapCols);
        _wrapCache[key] = wrapped;
        return wrapped;
    }

    private static int EstimateLineRows(string line, int wrapCols)
    {
        if (wrapCols <= 0)
        {
            return 1;
        }

        var len = line?.Length ?? 0;
        return Math.Max(1, (len + wrapCols - 1) / wrapCols);
    }

    private static WrappedLine WrapLine(string line, int wrapCols)
    {
        wrapCols = Math.Max(1, wrapCols);
        if (string.IsNullOrEmpty(line))
        {
            return new WrappedLine(1, new[] { new TextSpan(0, 0) });
        }

        var spans = new List<TextSpan>();
        var pos = 0;
        while (pos < line.Length)
        {
            var max = Math.Min(line.Length, pos + wrapCols);
            if (max == line.Length)
            {
                spans.Add(new TextSpan(pos, line.Length - pos));
                break;
            }

            var breakAt = -1;
            for (var i = max - 1; i > pos; i--)
            {
                if (char.IsWhiteSpace(line[i]))
                {
                    breakAt = i;
                    break;
                }
            }

            if (breakAt > pos)
            {
                var len = breakAt - pos;
                while (len > 0 && char.IsWhiteSpace(line[pos + len - 1]))
                {
                    len--;
                }
                spans.Add(new TextSpan(pos, Math.Max(0, len)));
                pos = breakAt + 1;
                while (pos < line.Length && char.IsWhiteSpace(line[pos]))
                {
                    pos++;
                }
            }
            else
            {
                spans.Add(new TextSpan(pos, max - pos));
                pos = max;
            }
        }

        if (spans.Count == 0)
        {
            spans.Add(new TextSpan(0, 0));
        }

        return new WrappedLine(spans.Count, spans.ToArray());
    }

    private static PxRect ExpandAndClamp(PxRect rect, int expandPx, PxRect bounds)
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

        if (x < bounds.X)
        {
            w -= bounds.X - x;
            x = bounds.X;
        }

        if (y < bounds.Y)
        {
            h -= bounds.Y - y;
            y = bounds.Y;
        }

        var maxW = Math.Max(0, (bounds.X + bounds.Width) - x);
        var maxH = Math.Max(0, (bounds.Y + bounds.Height) - y);
        w = Math.Clamp(w, 0, maxW);
        h = Math.Clamp(h, 0, maxH);
        return new PxRect(x, y, w, h);
    }

    public readonly record struct TextSpan(int Start, int Length);

    public sealed record WrappedLine(int RowCount, TextSpan[] Spans);
}
