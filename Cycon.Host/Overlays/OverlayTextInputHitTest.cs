using System;
using Cycon.Layout.Metrics;

namespace Cycon.Host.Overlays;

internal static class OverlayTextInputHitTest
{
    public static int HitTestIndex(in FixedCellGrid grid, int textStartXPx, int scrollXPx, int textLength, int mouseXPx)
    {
        var cellW = Math.Max(1, grid.CellWidthPx);
        var x = mouseXPx - textStartXPx + scrollXPx;
        var col = x <= 0 ? 0 : x / cellW;
        return Math.Clamp(col, 0, Math.Max(0, textLength));
    }

    public static int MeasureTextXPx(in FixedCellGrid grid, int index)
    {
        var cellW = Math.Max(1, grid.CellWidthPx);
        return Math.Max(0, index) * cellW;
    }
}

