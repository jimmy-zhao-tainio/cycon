using System;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;

namespace Cycon.Layout.Scene3D;

public static class Scene3DLayouter
{
    public static PxRect LayoutViewport(FixedCellGrid grid, int blockStartRowIndex, double preferredAspectRatio)
    {
        preferredAspectRatio = preferredAspectRatio <= 0 ? (16.0 / 9.0) : preferredAspectRatio;

        var width = Math.Max(0, grid.ContentWidthPx);
        var idealHeight = (int)Math.Round(width / preferredAspectRatio);
        return LayoutViewport(grid, blockStartRowIndex, desiredHeightPx: idealHeight);
    }

    public static PxRect LayoutViewport(FixedCellGrid grid, int blockStartRowIndex, int desiredHeightPx)
    {
        var width = Math.Max(0, grid.ContentWidthPx);

        var minHeight = Math.Max(1, grid.CellHeightPx);
        var maxHeight = Math.Max(1, grid.ContentHeightPx - (grid.CellHeightPx * 2));
        var height = Math.Max(minHeight, Math.Min(desiredHeightPx, maxHeight));

        var x = grid.PaddingLeftPx;
        var y = grid.PaddingTopPx + (blockStartRowIndex * grid.CellHeightPx);
        return new PxRect(x, y, width, height);
    }
}
