using System;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Input;
using Cycon.Host.Interaction;
using Cycon.Layout;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;

namespace Cycon.Host.Tests.Interaction;

public sealed class TabCompletionRoutingTests
{
    [Fact]
    public void TabDoesNotAutocompleteOwnedPrompt()
    {
        var transcript = new Transcript();
        var owned = new PromptBlock(new BlockId(1), "> ", new PromptOwner(OwnerId: 1, PromptId: 1))
        {
            Input = "he",
            CaretIndex = 2
        };
        transcript.Add(owned);

        var reducer = new InteractionReducer();
        reducer.Initialize(transcript);

        var actions = reducer.Handle(
            new InputEvent.KeyDown(HostKey.Tab, HostKeyModifiers.None),
            CreateEmptyFrame(),
            transcript);

        Assert.DoesNotContain(actions, a => a is HostAction.Autocomplete);
    }

    private static LayoutFrame CreateEmptyFrame()
    {
        var grid = new FixedCellGrid(
            framebufferWidthPx: 100,
            framebufferHeightPx: 100,
            cellWidthPx: 8,
            cellHeightPx: 16,
            cols: 10,
            rows: 10,
            paddingLeftPx: 0,
            paddingTopPx: 0,
            paddingRightPx: 0,
            paddingBottomPx: 0);
        var hit = new HitTestMap(grid, Array.Empty<HitTestLine>());
        var scrollbar = new ScrollbarLayout(false, default, default, default, default);
        return new LayoutFrame(grid, Array.Empty<LayoutLine>(), hit, totalRows: 0, scrollbar, Array.Empty<Scene3DViewportLayout>());
    }
}
