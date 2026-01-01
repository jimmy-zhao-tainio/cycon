using Cycon.Layout.Metrics;
using Cycon.Layout.Scene3D;
using Cycon.Layout.Scrolling;

namespace Cycon.Host.Tests.Layout;

public sealed class Scene3DLayouterTests
{
    [Fact]
    public void LayoutViewport_UsesPreferredAspectRatio()
    {
        var grid = new FixedCellGrid(
            framebufferWidthPx: 900,
            framebufferHeightPx: 600,
            cellWidthPx: 8,
            cellHeightPx: 16,
            cols: 100,
            rows: 30,
            paddingLeftPx: 5,
            paddingTopPx: 3,
            paddingRightPx: 0,
            paddingBottomPx: 0);

        var rect = Scene3DLayouter.LayoutViewport(grid, blockStartRowIndex: 0, preferredAspectRatio: 16.0 / 9.0);

        Assert.Equal(800, rect.Width);
        Assert.Equal(448, rect.Height);
        Assert.Equal(5, rect.X);
        Assert.Equal(3, rect.Y);
        Assert.True(new PxRect(rect.X, rect.Y, rect.Width, rect.Height).Width > 0);
    }

    [Fact]
    public void LayoutViewport_HasIntrinsicHeight()
    {
        var grid = new FixedCellGrid(
            framebufferWidthPx: 900,
            framebufferHeightPx: 120,
            cellWidthPx: 8,
            cellHeightPx: 16,
            cols: 100,
            rows: 7,
            paddingLeftPx: 5,
            paddingTopPx: 3,
            paddingRightPx: 0,
            paddingBottomPx: 0);

        var rect = Scene3DLayouter.LayoutViewport(grid, blockStartRowIndex: 0, preferredAspectRatio: 16.0 / 9.0);

        Assert.Equal(80, rect.Height);
    }

    [Fact]
    public void LayoutViewport_DoesNotShrinkBasedOnStartRow()
    {
        var grid = new FixedCellGrid(
            framebufferWidthPx: 900,
            framebufferHeightPx: 600,
            cellWidthPx: 8,
            cellHeightPx: 16,
            cols: 100,
            rows: 30,
            paddingLeftPx: 5,
            paddingTopPx: 3,
            paddingRightPx: 0,
            paddingBottomPx: 0);

        var rect = Scene3DLayouter.LayoutViewport(grid, blockStartRowIndex: 5, preferredAspectRatio: 16.0 / 9.0);

        Assert.Equal(448, rect.Height);
    }
}
