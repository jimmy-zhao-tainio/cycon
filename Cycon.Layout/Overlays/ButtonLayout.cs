using System;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;

namespace Cycon.Layout.Overlays;

public readonly record struct ButtonLayoutResult(
    PxRect OuterRectPx,
    PxRect LabelRectPx,
    int TotalCols);

public static class ButtonLayout
{
    // 3-row button:
    // Row 0: top border band (16px)
    // Row 1: label row
    // Row 2: bottom border band (16px)
    //
    // Width in columns:
    // 2 (left border band) + 1 (left pad) + labelCols + 1 (right pad) + 2 (right border band)
    public static ButtonLayoutResult LayoutRightAligned3Row(in FixedCellGrid grid, int rightX, int topY, string label)
    {
        label ??= string.Empty;
        var cellW = Math.Max(1, grid.CellWidthPx);
        var cellH = Math.Max(1, grid.CellHeightPx);

        var labelCols = Math.Max(0, label.Length);
        var totalCols = 2 + 1 + labelCols + 1 + 2;
        var w = totalCols * cellW;
        var h = 3 * cellH;

        var x = rightX - w;
        var y = topY;

        // Label sits in the middle row, after border band + 1-cell padding.
        var labelX = x + ((2 + 1) * cellW);
        var labelY = y + cellH;
        var labelRect = new PxRect(labelX, labelY, labelCols * cellW, cellH);

        return new ButtonLayoutResult(new PxRect(x, y, w, h), labelRect, totalCols);
    }
}
