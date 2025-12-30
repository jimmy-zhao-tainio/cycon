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
            scroll.ScrollRowsFromBottom = 0;
            scroll.ScrollOffsetRows = 0;
            return;
        }

        var maxScrollOffsetRows = Math.Max(0, layout.TotalRows - layout.Grid.Rows);
        scroll.ScrollOffsetRows = Math.Clamp(scroll.ScrollOffsetRows, 0, maxScrollOffsetRows);
        scroll.ScrollRowsFromBottom = maxScrollOffsetRows - scroll.ScrollOffsetRows;

        var topRow = Math.Clamp(scroll.ScrollOffsetRows, 0, layout.Lines.Count - 1);
        var topLine = layout.Lines[topRow];
        scroll.TopVisualLineAnchor = new TopVisualLineAnchor(topLine.BlockIndex, topLine.Start);
    }

    public static void RestoreFromAnchor(ScrollState scroll, LayoutFrame layout)
    {
        var maxScrollOffsetRows = Math.Max(0, layout.TotalRows - layout.Grid.Rows);

        if (scroll.IsFollowingTail)
        {
            scroll.ScrollOffsetRows = maxScrollOffsetRows;
            scroll.ScrollRowsFromBottom = 0;
            return;
        }

        if (scroll.TopVisualLineAnchor is not { } anchor || layout.Lines.Count == 0)
        {
            scroll.ScrollOffsetRows = Math.Clamp(scroll.ScrollOffsetRows, 0, maxScrollOffsetRows);
            scroll.ScrollRowsFromBottom = maxScrollOffsetRows - scroll.ScrollOffsetRows;
            return;
        }

        var anchoredRow = FindRowForAnchor(layout, anchor);
        scroll.ScrollOffsetRows = Math.Clamp(anchoredRow, 0, maxScrollOffsetRows);
        scroll.ScrollRowsFromBottom = maxScrollOffsetRows - scroll.ScrollOffsetRows;
    }

    private static int FindRowForAnchor(LayoutFrame layout, TopVisualLineAnchor anchor)
    {
        var firstRowInBlock = -1;
        var bestRow = -1;
        var bestStart = -1;

        foreach (var line in layout.Lines)
        {
            if (line.BlockIndex != anchor.BlockIndex)
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

