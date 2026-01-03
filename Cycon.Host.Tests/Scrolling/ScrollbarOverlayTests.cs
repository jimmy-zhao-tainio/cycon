using Cycon.Core;
using Cycon.Core.Scrolling;
using Cycon.Core.Selection;
using Cycon.Core.Settings;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Xunit;

namespace Cycon.Host.Tests.Scrolling;

public sealed class ScrollbarOverlayTests
{
    [Fact]
    public void ContentWidth_IsNotReduced_WhenScrollbarEnabled()
    {
        var viewport = new ConsoleViewport(960, 540);
        var layoutSettings = new LayoutSettings
        {
            CellWidthPx = 8,
            CellHeightPx = 16,
            BorderLeftRightPx = 5,
            BorderTopBottomPx = 3,
            PaddingPolicy = PaddingPolicy.None
        };

        var disabled = CreateDocument(lineCount: 200, thicknessPx: 0);
        var enabled = CreateDocument(lineCount: 200, thicknessPx: 10);

        var layoutDisabled = new LayoutEngine().Layout(disabled, layoutSettings, viewport);
        var layoutEnabled = new LayoutEngine().Layout(enabled, layoutSettings, viewport);

        Assert.Equal(layoutDisabled.Grid.ContentWidthPx, layoutEnabled.Grid.ContentWidthPx);
        Assert.Equal(layoutDisabled.Grid.Cols, layoutEnabled.Grid.Cols);
    }

    [Fact]
    public void ScrollbarTrack_IsFlushRight_ToFramebufferEdge()
    {
        var viewport = new ConsoleViewport(960, 540);
        var layoutSettings = new LayoutSettings
        {
            CellWidthPx = 8,
            CellHeightPx = 16,
            BorderLeftRightPx = 5,
            BorderTopBottomPx = 3,
            PaddingPolicy = PaddingPolicy.None
        };

        const int thickness = 10;
        var document = CreateDocument(lineCount: 200, thicknessPx: thickness);
        var layout = new LayoutEngine().Layout(document, layoutSettings, viewport);
        var sb = layout.Scrollbar;

        Assert.True(sb.IsScrollable);
        Assert.Equal(layout.Grid.FramebufferWidthPx - thickness, sb.TrackRectPx.X);
        Assert.Equal(layout.Grid.FramebufferWidthPx, sb.TrackRectPx.X + sb.TrackRectPx.Width);
        Assert.Equal(layout.Grid.FramebufferWidthPx, sb.HitTrackRectPx.X + sb.HitTrackRectPx.Width);
        Assert.True(sb.HitTrackRectPx.X <= sb.TrackRectPx.X);
    }

    private static ConsoleDocument CreateDocument(int lineCount, int thicknessPx)
    {
        var transcript = new Transcript();
        for (var i = 0; i < lineCount; i++)
        {
            transcript.Add(new TextBlock(new BlockId(i + 1), $"line {i}", ConsoleTextStream.Stdout));
        }

        var settings = new ConsoleSettings
        {
            Scrollbar = new ScrollbarSettings
            {
                ThicknessPx = thicknessPx,
                MarginPx = 0
            }
        };

        return new ConsoleDocument(
            transcript,
            new InputState(),
            new ScrollState(),
            new SelectionState(),
            settings);
    }
}
