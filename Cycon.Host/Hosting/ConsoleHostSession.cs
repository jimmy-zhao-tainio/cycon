using System;
using System.Diagnostics;
using Cycon.Backends.Abstractions.Rendering;
using Cycon.Core;
using Cycon.Core.Metrics;
using Cycon.Core.Scrolling;
using Cycon.Core.Selection;
using Cycon.Core.Settings;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Services;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;
using Cycon.Rendering.Renderer;

namespace Cycon.Host.Hosting;

public sealed class ConsoleHostSession
{
    private readonly int _resizeSettleMs;
    private readonly int _rebuildThrottleMs;

    private readonly ConsoleDocument _document;
    private readonly LayoutSettings _layoutSettings;
    private readonly LayoutEngine _layoutEngine;
    private readonly ConsoleRenderer _renderer;
    private readonly Cycon.Rendering.Glyphs.GlyphAtlas _atlas;
    private readonly GlyphAtlasData _atlasData;

    private LayoutFrame? _lastLayout;
    private RenderFrame? _lastFrame;
    private GridSize _renderedGrid;
    private bool _pendingResizeRebuild;
    private bool _resizeVsyncDisabled;
    private long _lastRebuildTicks;
    private int _latestFramebufferWidth;
    private int _latestFramebufferHeight;
    private long _lastFramebufferChangeTicks;
    private GridSize _latestGrid;
    private long _lastGridChangeTicks;
    private bool _logged;
    private bool _initialized;
    private bool? _pendingSetVSync;
    private readonly int _cellWidthPx;
    private readonly int _cellHeightPx;

    private ConsoleHostSession(string text, int resizeSettleMs, int rebuildThrottleMs)
    {
        _resizeSettleMs = resizeSettleMs;
        _rebuildThrottleMs = rebuildThrottleMs;

        _document = CreateDocument(text);
        _layoutSettings = new LayoutSettings();
        var fontService = new FontService();
        _atlas = fontService.LoadVgaAtlas(_layoutSettings);
        _layoutSettings.CellWidthPx = _atlas.CellWidthPx;
        _layoutSettings.CellHeightPx = _atlas.CellHeightPx;
        _layoutSettings.PaddingPolicy = PaddingPolicy.None;
        _cellWidthPx = _layoutSettings.CellWidthPx;
        _cellHeightPx = _layoutSettings.CellHeightPx;

        _atlasData = RenderFrameAdapter.Adapt(_atlas);
        _renderer = new ConsoleRenderer();
        _layoutEngine = new LayoutEngine();
    }

    public static ConsoleHostSession CreateVga(string text, int resizeSettleMs = 80, int rebuildThrottleMs = 80)
    {
        return new ConsoleHostSession(text, resizeSettleMs, rebuildThrottleMs);
    }

    public GlyphAtlasData Atlas => _atlasData;

    public void Initialize(int initialFbW, int initialFbH)
    {
        _latestFramebufferWidth = initialFbW;
        _latestFramebufferHeight = initialFbH;
        _latestGrid = ComputeGrid(_latestFramebufferWidth, _latestFramebufferHeight, _layoutSettings);
        var nowTicks = Stopwatch.GetTimestamp();
        _lastFramebufferChangeTicks = nowTicks;
        _lastGridChangeTicks = nowTicks;
        _initialized = true;
    }

    public void OnFramebufferResized(int fbW, int fbH)
    {
        var nowTicks = Stopwatch.GetTimestamp();
        HandleFramebufferChanged(fbW, fbH, nowTicks);
    }

    public FrameTickResult Tick()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Session must be initialized before ticking.");
        }

        var nowTicks = Stopwatch.GetTimestamp();
        var framebufferWidth = _latestFramebufferWidth;
        var framebufferHeight = _latestFramebufferHeight;
        var currentGrid = _latestGrid;

        var elapsedSinceFramebufferChangeMs = (nowTicks - _lastFramebufferChangeTicks) * 1000.0 / Stopwatch.Frequency;
        var framebufferSettled = elapsedSinceFramebufferChangeMs >= _resizeSettleMs;

        var elapsedSinceGridChangeMs = (nowTicks - _lastGridChangeTicks) * 1000.0 / Stopwatch.Frequency;
        var gridSettled = elapsedSinceGridChangeMs >= _resizeSettleMs;

        if (framebufferSettled)
        {
            _document.Scroll.TopVisualLineAnchor = null;

            if (_resizeVsyncDisabled)
            {
                _pendingSetVSync = true;
                _resizeVsyncDisabled = false;
            }
        }

        var renderedGrid = _lastFrame?.BuiltGrid ?? default;

        var gridMismatch = _lastFrame is null || renderedGrid != currentGrid;
        if (gridMismatch) _pendingResizeRebuild = true;

        var elapsedSinceRebuildMs = (nowTicks - _lastRebuildTicks) * 1000.0 / Stopwatch.Frequency;
        var shouldRebuild = _lastFrame is null
            || (_pendingResizeRebuild && gridMismatch)
            || (gridMismatch && (gridSettled || elapsedSinceRebuildMs >= _rebuildThrottleMs));

        if (shouldRebuild)
        {
            var restoreAnchor = _pendingResizeRebuild;

            var snapW = framebufferWidth;
            var snapH = framebufferHeight;
            var snapGrid = currentGrid;

            var rebuildPasses = gridSettled && gridMismatch ? 2 : 1;
            for (var pass = 0; pass < rebuildPasses; pass++)
            {
                var passNowTicks = Stopwatch.GetTimestamp();

                var (frame, builtGrid, layout, renderFrame) = BuildFrameFor(
                    snapW,
                    snapH,
                    restoreAnchor);

                _lastFrame = frame;
                _renderedGrid = builtGrid;
                _lastLayout = layout;
                LogOnce(_atlas, layout, renderFrame, snapW, snapH);
                _lastRebuildTicks = passNowTicks;

                var verifyGrid = _latestGrid;
                if (_renderedGrid == verifyGrid)
                {
                    break;
                }

                snapW = _latestFramebufferWidth;
                snapH = _latestFramebufferHeight;
                snapGrid = verifyGrid;
            }

            _pendingResizeRebuild = false;

            framebufferWidth = snapW;
            framebufferHeight = snapH;
            currentGrid = snapGrid;
        }

        if (_lastFrame is null)
        {
            throw new InvalidOperationException("Tick invariant violated: frame must be available.");
        }

        var setVSync = _pendingSetVSync;
        _pendingSetVSync = null;
        return new FrameTickResult(framebufferWidth, framebufferHeight, _lastFrame, setVSync);
    }

    private static ConsoleDocument CreateDocument(string text)
    {
        var transcript = new Transcript();
        var span = new TextSpan(text, new TextStyle());
        transcript.Add(new TextBlock(new[] { span }));

        return new ConsoleDocument(
            transcript,
            new InputState(),
            new ScrollState(),
            new SelectionState(),
            new ConsoleSettings());
    }

    private void LogOnce(
        Cycon.Rendering.Glyphs.GlyphAtlas atlas,
        LayoutFrame layout,
        Cycon.Rendering.RenderFrame renderFrame,
        int framebufferWidth,
        int framebufferHeight)
    {
        if (_logged)
        {
            return;
        }

        var nonZero = CountNonZero(atlas.Pixels);
        var glyphCount = CountGlyphs(renderFrame);

        Console.WriteLine($"Atlas {atlas.Width}x{atlas.Height} cell={atlas.CellWidthPx}x{atlas.CellHeightPx} baseline={atlas.BaselinePx} nonZero={nonZero}");
        Console.WriteLine($"Grid cols={layout.Grid.Cols} rows={layout.Grid.Rows} padding={layout.Grid.PaddingLeftPx},{layout.Grid.PaddingTopPx}");
        Console.WriteLine($"Lines={layout.Lines.Count} Commands={renderFrame.Commands.Count} Glyphs={glyphCount}");
        Console.WriteLine($"Window {framebufferWidth}x{framebufferHeight} Framebuffer {framebufferWidth}x{framebufferHeight}");

        _logged = true;
    }

    private static int CountNonZero(byte[] data)
    {
        var count = 0;
        foreach (var value in data)
        {
            if (value != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountGlyphs(Cycon.Rendering.RenderFrame frame)
    {
        var total = 0;
        foreach (var command in frame.Commands)
        {
            if (command is Cycon.Rendering.Commands.DrawGlyphRun run)
            {
                total += run.Glyphs.Count;
            }
        }

        return total;
    }

    private static GridSize ComputeGrid(int framebufferWidth, int framebufferHeight, LayoutSettings settings)
    {
        var cellW = settings.CellWidthPx;
        var cellH = settings.CellHeightPx;

        var cols = cellW > 0 ? framebufferWidth / cellW : 0;
        var rows = cellH > 0 ? framebufferHeight / cellH : 0;

        return new GridSize(cols, rows);
    }

    private void HandleFramebufferChanged(int width, int height, long nowTicks)
    {
        var changed = width != _latestFramebufferWidth || height != _latestFramebufferHeight;
        _latestFramebufferWidth = width;
        _latestFramebufferHeight = height;

        if (!changed)
        {
            return;
        }

        _lastFramebufferChangeTicks = nowTicks;

        var cols = _cellWidthPx > 0 ? width / _cellWidthPx : 0;
        var rows = _cellHeightPx > 0 ? height / _cellHeightPx : 0;
        var newGrid = new GridSize(cols, rows);

        if (newGrid != _latestGrid)
        {
            _latestGrid = newGrid;
            _lastGridChangeTicks = nowTicks;
            _pendingResizeRebuild = true;
        }

        if (_resizeVsyncDisabled)
        {
            return;
        }

        if (_lastLayout is not null)
        {
            ScrollAnchoring.CaptureAnchor(_document.Scroll, _lastLayout);
        }

        _pendingSetVSync = false;
        _resizeVsyncDisabled = true;
    }

    private (RenderFrame Frame, GridSize BuiltGrid, LayoutFrame Layout, Cycon.Rendering.RenderFrame RenderFrame)
        BuildFrameFor(
            int framebufferWidth,
            int framebufferHeight,
            bool restoreAnchor)
    {
        var viewport = new ConsoleViewport(framebufferWidth, framebufferHeight);
        var layout = _layoutEngine.Layout(_document, _layoutSettings, viewport);
        if (restoreAnchor)
        {
            ScrollAnchoring.RestoreFromAnchor(_document.Scroll, layout);
        }

        var renderFrame = _renderer.Render(_document, layout, _atlas);
        var backendFrame = RenderFrameAdapter.Adapt(renderFrame);
        var builtGrid = backendFrame.BuiltGrid;
        return (backendFrame, builtGrid, layout, renderFrame);
    }
}

public readonly record struct FrameTickResult(
    int FramebufferWidth,
    int FramebufferHeight,
    RenderFrame Frame,
    bool? SetVSync);
