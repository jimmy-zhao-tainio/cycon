using System;
using Cycon.Render;

namespace Cycon.Layout.Inspect;

public static class InspectLayoutEngine
{
    public static bool TryCompute(
        in RectPx outerRectPx,
        in TextMetrics textMetrics,
        int outerBorderPx,
        InspectPanelSpec[] panels,
        out InspectLayoutResult layout)
    {
        layout = default;

        var cellW = Math.Max(1, textMetrics.CellWidthPx);
        var cellH = Math.Max(1, textMetrics.CellHeightPx);
        outerBorderPx = Math.Max(0, outerBorderPx);

        if (outerRectPx.Width <= 0 || outerRectPx.Height <= 0)
        {
            return false;
        }

        var gridOuter = DeflateRect(outerRectPx, outerBorderPx);
        if (gridOuter.Width <= 0 || gridOuter.Height <= 0)
        {
            return false;
        }

        var cols = Math.Max(0, gridOuter.Width / cellW);
        var rows = Math.Max(0, gridOuter.Height / cellH);
        if (cols <= 0 || rows <= 0)
        {
            return false;
        }

        var gridW = cols * cellW;
        var gridH = rows * cellH;
        var gridRect = new RectPx(gridOuter.X, gridOuter.Y, gridW, gridH);

        var topCells = 0;
        var bottomCells = 0;
        var leftCells = 0;
        var rightCells = 0;

        if (panels is not null)
        {
            for (var i = 0; i < panels.Length; i++)
            {
                var size = Math.Max(0, panels[i].SizeCells);
                switch (panels[i].Edge)
                {
                    case InspectEdge.Top:
                        topCells = Math.Max(topCells, size);
                        break;
                    case InspectEdge.Bottom:
                        bottomCells = Math.Max(bottomCells, size);
                        break;
                    case InspectEdge.Left:
                        leftCells = Math.Max(leftCells, size);
                        break;
                    case InspectEdge.Right:
                        rightCells = Math.Max(rightCells, size);
                        break;
                }
            }
        }

        topCells = Math.Min(topCells, rows);
        bottomCells = Math.Min(bottomCells, Math.Max(0, rows - topCells));
        var midRows = Math.Max(0, rows - topCells - bottomCells);

        leftCells = Math.Min(leftCells, cols);
        rightCells = Math.Min(rightCells, Math.Max(0, cols - leftCells));
        var midCols = Math.Max(0, cols - leftCells - rightCells);

        if (midRows <= 0 || midCols <= 0)
        {
            return false;
        }

        RectPx? topPanel = null;
        RectPx? bottomPanel = null;
        RectPx? leftPanel = null;
        RectPx? rightPanel = null;

        var content = gridRect;

        if (topCells > 0)
        {
            var h = topCells * cellH;
            topPanel = new RectPx(gridRect.X, gridRect.Y, gridRect.Width, h);
            content = new RectPx(content.X, content.Y + h, content.Width, Math.Max(0, content.Height - h));
        }

        if (bottomCells > 0)
        {
            var h = bottomCells * cellH;
            bottomPanel = new RectPx(gridRect.X, gridRect.Y + gridRect.Height - h, gridRect.Width, h);
            content = new RectPx(content.X, content.Y, content.Width, Math.Max(0, content.Height - h));
        }

        if (leftCells > 0)
        {
            var w = leftCells * cellW;
            leftPanel = new RectPx(content.X, content.Y, w, content.Height);
            content = new RectPx(content.X + w, content.Y, Math.Max(0, content.Width - w), content.Height);
        }

        if (rightCells > 0)
        {
            var w = rightCells * cellW;
            rightPanel = new RectPx(content.X + content.Width - w, content.Y, w, content.Height);
            content = new RectPx(content.X, content.Y, Math.Max(0, content.Width - w), content.Height);
        }

        if (content.Width <= 0 || content.Height <= 0)
        {
            return false;
        }

        layout = new InspectLayoutResult(
            OuterRect: outerRectPx,
            ContentRect: content,
            TopPanelRect: topPanel,
            BottomPanelRect: bottomPanel,
            LeftPanelRect: leftPanel,
            RightPanelRect: rightPanel,
            GridOriginPx: new PointPx(gridRect.X, gridRect.Y),
            CellW: cellW,
            CellH: cellH);
        return true;
    }

    private static RectPx DeflateRect(in RectPx rect, int inset)
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
}
