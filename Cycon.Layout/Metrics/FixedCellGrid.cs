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

        var borderX = Math.Max(0, settings.BorderLeftRightPx);
        var borderY = Math.Max(0, settings.BorderTopBottomPx);
        var rightGutter = Math.Max(0, settings.RightGutterPx);
        var availableWidth = Math.Max(0, viewport.FramebufferWidthPx - (borderX * 2) - rightGutter);
        var availableHeight = Math.Max(0, viewport.FramebufferHeightPx - (borderY * 2));

        var cols = settings.CellWidthPx > 0 ? availableWidth / settings.CellWidthPx : 0;
        var rows = settings.CellHeightPx > 0 ? availableHeight / settings.CellHeightPx : 0;

        var usedWidth = cols * settings.CellWidthPx;
        var usedHeight = rows * settings.CellHeightPx;

        var leftoverX = availableWidth - usedWidth;
        var leftoverY = availableHeight - usedHeight;

        var paddingLeft = borderX;
        var paddingTop = borderY;
        var paddingRight = borderX + rightGutter + leftoverX;
        var paddingBottom = borderY + leftoverY;

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
