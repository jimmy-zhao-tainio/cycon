using System;
using System.Diagnostics;
using Cycon.Core.Metrics;
using Cycon.Core.Settings;
using Cycon.Layout;
using Cycon.Layout.Metrics;

namespace Cycon.Host.Hosting;

internal sealed class ResizeCoordinator
{
    private readonly int _resizeSettleMs;
    private readonly int _rebuildThrottleMs;
    private readonly LayoutSettings _layoutSettings;

    private int _latestFramebufferWidth;
    private int _latestFramebufferHeight;
    private GridSize _latestGrid;
    private long _lastFramebufferChangeTicks;
    private long _lastGridChangeTicks;
    private bool _resizeVsyncDisabled;
    private bool _pendingResizeRebuild;
    private long _lastRebuildTicks;
    private int _lastBuiltFramebufferWidth;
    private int _lastBuiltFramebufferHeight;
    private GridSize _renderedGrid;
    private bool? _pendingSetVSync;

    public ResizeCoordinator(int resizeSettleMs, int rebuildThrottleMs, LayoutSettings layoutSettings)
    {
        _resizeSettleMs = resizeSettleMs;
        _rebuildThrottleMs = rebuildThrottleMs;
        _layoutSettings = layoutSettings;
    }

    public void Initialize(int framebufferWidth, int framebufferHeight, long nowTicks)
    {
        _latestFramebufferWidth = framebufferWidth;
        _latestFramebufferHeight = framebufferHeight;
        _latestGrid = ComputeGrid(framebufferWidth, framebufferHeight, _layoutSettings);
        _lastFramebufferChangeTicks = nowTicks;
        _lastGridChangeTicks = nowTicks;
    }

    public ResizeEventResult OnFramebufferResized(
        int framebufferWidth,
        int framebufferHeight,
        long nowTicks,
        LayoutFrame? lastLayout)
    {
        var changed = framebufferWidth != _latestFramebufferWidth || framebufferHeight != _latestFramebufferHeight;
        _latestFramebufferWidth = framebufferWidth;
        _latestFramebufferHeight = framebufferHeight;

        if (!changed)
        {
            return default;
        }

        _lastFramebufferChangeTicks = nowTicks;

        var newGrid = ComputeGrid(framebufferWidth, framebufferHeight, _layoutSettings);
        if (newGrid != _latestGrid)
        {
            _latestGrid = newGrid;
            _lastGridChangeTicks = nowTicks;
            _pendingResizeRebuild = true;
        }

        if (_resizeVsyncDisabled)
        {
            return default;
        }

        var captureAnchor = lastLayout is not null;
        _pendingSetVSync = false;
        _resizeVsyncDisabled = true;

        return new ResizeEventResult(captureAnchor);
    }

    public ResizePlan PlanForTick(long nowTicks, bool hasFrame, GridSize renderedGrid, bool pendingContentRebuild)
    {
        var elapsedSinceFramebufferChangeMs = (nowTicks - _lastFramebufferChangeTicks) * 1000.0 / Stopwatch.Frequency;
        var framebufferSettled = elapsedSinceFramebufferChangeMs >= _resizeSettleMs;
        var clearTopAnchor = false;

        if (framebufferSettled && _resizeVsyncDisabled)
        {
            _pendingSetVSync = true;
            _resizeVsyncDisabled = false;
            clearTopAnchor = true;
        }

        var elapsedSinceGridChangeMs = (nowTicks - _lastGridChangeTicks) * 1000.0 / Stopwatch.Frequency;
        var gridSettled = elapsedSinceGridChangeMs >= _resizeSettleMs;

        var gridMismatch = !hasFrame || renderedGrid != _latestGrid;
        if (gridMismatch)
        {
            _pendingResizeRebuild = true;
        }

        var framebufferMismatch = !hasFrame
            || _lastBuiltFramebufferWidth != _latestFramebufferWidth
            || _lastBuiltFramebufferHeight != _latestFramebufferHeight;

        var elapsedSinceRebuildMs = (nowTicks - _lastRebuildTicks) * 1000.0 / Stopwatch.Frequency;
        var shouldRebuild = !hasFrame
            || pendingContentRebuild
            || (_pendingResizeRebuild && gridMismatch)
            || (gridMismatch && (gridSettled || elapsedSinceRebuildMs >= _rebuildThrottleMs))
            || (framebufferMismatch && (framebufferSettled || elapsedSinceRebuildMs >= _rebuildThrottleMs));

        var rebuildPasses = gridSettled && gridMismatch ? 2 : 1;

        return new ResizePlan(
            _latestFramebufferWidth,
            _latestFramebufferHeight,
            _latestGrid,
            framebufferSettled,
            gridSettled,
            gridMismatch,
            framebufferMismatch,
            shouldRebuild,
            rebuildPasses,
            _pendingResizeRebuild,
            clearTopAnchor,
            elapsedSinceRebuildMs);
    }

    public void NotifyFrameBuilt(int framebufferWidth, int framebufferHeight, GridSize builtGrid, long nowTicks)
    {
        _lastBuiltFramebufferWidth = framebufferWidth;
        _lastBuiltFramebufferHeight = framebufferHeight;
        _renderedGrid = builtGrid;
        _lastRebuildTicks = nowTicks;
    }

    public void UpdateRenderedGrid(GridSize grid)
    {
        _renderedGrid = grid;
    }

    public void ClearPendingResizeRebuild()
    {
        _pendingResizeRebuild = false;
    }

    public ResizeSnapshot GetLatestSnapshot() =>
        new(_latestFramebufferWidth, _latestFramebufferHeight, _latestGrid);

    public ResizeSnapshot GetBuiltSnapshot() =>
        new(_lastBuiltFramebufferWidth, _lastBuiltFramebufferHeight, _renderedGrid);

    public bool? ConsumeVSyncRequest()
    {
        var pending = _pendingSetVSync;
        _pendingSetVSync = null;
        return pending;
    }

    private static GridSize ComputeGrid(int framebufferWidth, int framebufferHeight, LayoutSettings settings)
    {
        var viewport = new ConsoleViewport(framebufferWidth, framebufferHeight);
        var grid = FixedCellGrid.FromViewport(viewport, settings);
        return new GridSize(grid.Cols, grid.Rows);
    }
}

internal readonly record struct ResizePlan(
    int FramebufferWidth,
    int FramebufferHeight,
    GridSize CurrentGrid,
    bool FramebufferSettled,
    bool GridSettled,
    bool GridMismatch,
    bool FramebufferMismatch,
    bool ShouldRebuild,
    int RebuildPasses,
    bool PendingResizeRebuild,
    bool ClearTopAnchor,
    double ElapsedSinceRebuildMs);

internal readonly record struct ResizeEventResult(bool CaptureAnchor);

internal readonly record struct ResizeSnapshot(
    int FramebufferWidth,
    int FramebufferHeight,
    GridSize Grid);
