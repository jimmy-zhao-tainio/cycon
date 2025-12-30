using System;
using Cycon.Layout;

namespace Cycon.Layout.Metrics;

public readonly struct FixedCellGrid
{
    public FixedCellGrid(
        int framebufferWidthPx,
        int framebufferHeightPx,
        int cellWidthPx,
        int cellHeightPx,
        int cols,
        int rows,
        int paddingLeftPx,
        int paddingTopPx,
        int paddingRightPx,
        int paddingBottomPx)
    {
        FramebufferWidthPx = framebufferWidthPx;
        FramebufferHeightPx = framebufferHeightPx;
        CellWidthPx = cellWidthPx;
        CellHeightPx = cellHeightPx;
        Cols = cols;
        Rows = rows;
        PaddingLeftPx = paddingLeftPx;
        PaddingTopPx = paddingTopPx;
        PaddingRightPx = paddingRightPx;
        PaddingBottomPx = paddingBottomPx;
        ContentWidthPx = cols * cellWidthPx;
        ContentHeightPx = rows * cellHeightPx;
    }

    public int FramebufferWidthPx { get; }
    public int FramebufferHeightPx { get; }
    public int CellWidthPx { get; }
    public int CellHeightPx { get; }
    public int Cols { get; }
    public int Rows { get; }
    public int PaddingLeftPx { get; }
    public int PaddingTopPx { get; }
    public int PaddingRightPx { get; }
    public int PaddingBottomPx { get; }
    public int ContentWidthPx { get; }
    public int ContentHeightPx { get; }

    public static FixedCellGrid FromViewport(ConsoleViewport viewport, LayoutSettings settings)
    {
        if (settings.CellWidthPx <= 0 || settings.CellHeightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Cell metrics must be positive.");
        }

        var cols = viewport.FramebufferWidthPx / settings.CellWidthPx;
        var rows = viewport.FramebufferHeightPx / settings.CellHeightPx;

        var usedWidth = cols * settings.CellWidthPx;
        var usedHeight = rows * settings.CellHeightPx;

        var leftoverX = viewport.FramebufferWidthPx - usedWidth;
        var leftoverY = viewport.FramebufferHeightPx - usedHeight;

        var paddingLeft = 0;
        var paddingTop = 0;
        var paddingRight = leftoverX;
        var paddingBottom = leftoverY;

        return new FixedCellGrid(
            viewport.FramebufferWidthPx,
            viewport.FramebufferHeightPx,
            settings.CellWidthPx,
            settings.CellHeightPx,
            cols,
            rows,
            paddingLeft,
            paddingTop,
            paddingRight,
            paddingBottom);
    }
}
