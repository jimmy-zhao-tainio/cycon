using System;
using Cycon.Core.Scrolling;

namespace Cycon.Layout.Scrolling;

public static class ScrollAnchoring
{
    public static void CaptureAnchor(ScrollState scroll, LayoutFrame layout)
    {
        if (layout.Lines.Count == 0)
        {
            scroll.TopVisualLineAnchor = null;
            scroll.ScrollPxFromBottom = 0;
            scroll.ScrollOffsetPx = 0;
            return;
        }

        var cellH = layout.Grid.CellHeightPx;
        var maxScrollOffsetRows = Math.Max(0, layout.TotalRows - layout.Grid.Rows);
        var maxScrollOffsetPx = Math.Max(0, maxScrollOffsetRows * cellH);
        scroll.ScrollOffsetPx = Math.Clamp(scroll.ScrollOffsetPx, 0, maxScrollOffsetPx);
        scroll.ScrollPxFromBottom = maxScrollOffsetPx - scroll.ScrollOffsetPx;

        var topRow = Math.Clamp(cellH <= 0 ? 0 : scroll.ScrollOffsetPx / cellH, 0, layout.Lines.Count - 1);
        var topLine = layout.Lines[topRow];
        scroll.TopVisualLineAnchor = new TopVisualLineAnchor(topLine.BlockId, topLine.Start);
    }

    public static void RestoreFromAnchor(ScrollState scroll, LayoutFrame layout)
    {
        var cellH = layout.Grid.CellHeightPx;
        var maxScrollOffsetRows = Math.Max(0, layout.TotalRows - layout.Grid.Rows);
        var maxScrollOffsetPx = Math.Max(0, maxScrollOffsetRows * cellH);

        if (scroll.IsFollowingTail)
        {
            scroll.ScrollOffsetPx = maxScrollOffsetPx;
            scroll.ScrollPxFromBottom = 0;
            return;
        }

        if (scroll.TopVisualLineAnchor is not { } anchor || layout.Lines.Count == 0)
        {
            scroll.ScrollOffsetPx = Math.Clamp(scroll.ScrollOffsetPx, 0, maxScrollOffsetPx);
            scroll.ScrollPxFromBottom = maxScrollOffsetPx - scroll.ScrollOffsetPx;
            return;
        }

        var anchoredRow = FindRowForAnchor(layout, anchor);
        scroll.ScrollOffsetPx = Math.Clamp(anchoredRow * cellH, 0, maxScrollOffsetPx);
        scroll.ScrollPxFromBottom = maxScrollOffsetPx - scroll.ScrollOffsetPx;
    }

    private static int FindRowForAnchor(LayoutFrame layout, TopVisualLineAnchor anchor)
    {
        var firstRowInBlock = -1;
        var bestRow = -1;
        var bestStart = -1;

        foreach (var line in layout.Lines)
        {
            if (line.BlockId != anchor.BlockId)
            {
                continue;
            }

            if (firstRowInBlock < 0)
            {
                firstRowInBlock = line.RowIndex;
            }

            if (line.Length > 0 && anchor.CharIndex >= line.Start && anchor.CharIndex < line.Start + line.Length)
            {
                return line.RowIndex;
            }

            if (line.Start <= anchor.CharIndex && line.Start >= bestStart)
            {
                bestStart = line.Start;
                bestRow = line.RowIndex;
            }
        }

        if (bestRow >= 0)
        {
            return bestRow;
        }

        return Math.Max(0, firstRowInBlock);
    }
}
