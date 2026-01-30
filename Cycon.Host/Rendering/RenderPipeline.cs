using System;
using System.Collections.Generic;
using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Metrics;
using Cycon.Core.Settings;
using Cycon.Core.Transcript;
using Cycon.Layout;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Layout.Overlays;
using Cycon.Layout.Scrolling;
using Cycon.Rendering.Renderer;
using Cycon.Rendering.Styling;
using Cycon.Host.Services;

namespace Cycon.Host.Rendering;

internal sealed class RenderPipeline
{
    private readonly LayoutEngine _layoutEngine;
    private readonly ConsoleRenderer _renderer;
    private readonly IConsoleFont _font;
    private readonly SelectionStyle _selectionStyle;

    public RenderPipeline(
        LayoutEngine layoutEngine,
        ConsoleRenderer renderer,
        IConsoleFont font,
        SelectionStyle selectionStyle)
    {
        _layoutEngine = layoutEngine;
        _renderer = renderer;
        _font = font;
        _selectionStyle = selectionStyle;
    }

    public RenderPipelineResult BuildFrame(
        ConsoleDocument document,
        LayoutSettings layoutSettings,
        ConsoleViewport viewport,
        bool restoreAnchor,
        byte caretAlpha,
        double timeSeconds,
        IReadOnlyDictionary<BlockId, BlockId> commandIndicators,
        IReadOnlyList<int>? meshReleases,
        BlockId? focusedViewportBlockId = null,
        UIActionState uiActions = default,
        OverlaySlabFrame? overlaySlab = null,
        UIActionState overlayActions = default)
    {
        var layout = _layoutEngine.Layout(document, layoutSettings, viewport);
        if (restoreAnchor)
        {
            ScrollAnchoring.RestoreFromAnchor(document.Scroll, layout);
        }

        var maxScrollOffsetRows = layout.Grid.Rows <= 0
            ? 0
            : Math.Max(0, layout.TotalRows - layout.Grid.Rows);
        var cellH = layout.Grid.CellHeightPx;
        var maxScrollOffsetPx = Math.Max(0, maxScrollOffsetRows * cellH);

        if (document.Scroll.IsFollowingTail)
        {
            if (document.Scroll.ScrollOffsetPx != maxScrollOffsetPx)
            {
                document.Scroll.ScrollOffsetPx = maxScrollOffsetPx;
                document.Scroll.ScrollPxFromBottom = 0;
            }
        }
        else
        {
            document.Scroll.ScrollOffsetPx = Math.Clamp(document.Scroll.ScrollOffsetPx, 0, maxScrollOffsetPx);
            document.Scroll.ScrollPxFromBottom = maxScrollOffsetPx - document.Scroll.ScrollOffsetPx;
        }

        var scrollOffsetRowsForScrollbar = cellH <= 0
            ? 0
            : Math.Clamp(document.Scroll.ScrollOffsetPx / cellH, 0, maxScrollOffsetRows);

        var scrollbar = ScrollbarLayouter.Layout(
            layout.Grid,
            layout.TotalRows,
            scrollOffsetRowsForScrollbar,
            document.Settings.Scrollbar);

        if (scrollbar != layout.Scrollbar)
        {
            layout = new LayoutFrame(layout.Grid, layout.Lines, layout.HitTestMap, layout.TotalRows, scrollbar, layout.Scene3DViewports);
        }

        var renderFrame = _renderer.Render(
            document,
            layout,
            _font,
            _selectionStyle,
            timeSeconds: timeSeconds,
            commandIndicators: commandIndicators,
            caretAlpha: caretAlpha,
            meshReleases: meshReleases,
            focusedViewportBlockId: focusedViewportBlockId,
            uiActions: uiActions,
            overlaySlab: overlaySlab,
            overlayActions: overlayActions,
            renderMuted: overlaySlab is { IsModal: true } || focusedViewportBlockId is not null);

        var backendFrame = RenderFrameAdapter.Adapt(renderFrame);
        return new RenderPipelineResult(backendFrame, backendFrame.BuiltGrid, layout, renderFrame);
    }
}

internal readonly record struct RenderPipelineResult(
    Cycon.Backends.Abstractions.Rendering.RenderFrame BackendFrame,
    GridSize BuiltGrid,
    LayoutFrame Layout,
    Cycon.Rendering.RenderFrame RenderFrame);
