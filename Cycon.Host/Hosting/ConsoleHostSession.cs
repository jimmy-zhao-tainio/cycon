using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Backends.Abstractions;
using Cycon.Backends.Abstractions.Rendering;
using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Metrics;
using Cycon.Core.Scrolling;
using Cycon.Core.Selection;
using Cycon.Core.Settings;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Commands;
using Cycon.Host.Commands.Input;
using Cycon.Host.Commands.Blocks;
using Cycon.Host.Commands.Handlers;
using Cycon.Host.Interaction;
using Cycon.Host.Input;
using Cycon.Host.Services;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;
using Cycon.Rendering.Renderer;
using Cycon.Rendering.Styling;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Hosting;

public sealed class ConsoleHostSession
{
    private readonly int _resizeSettleMs;
    private readonly int _rebuildThrottleMs;

    private readonly ConsoleDocument _document;
    private readonly LayoutSettings _layoutSettings;
    private readonly LayoutEngine _layoutEngine;
    private readonly ConsoleRenderer _renderer;
    private readonly IConsoleFont _font;
    private readonly GlyphAtlasData _atlasData;
    private readonly SelectionStyle _selectionStyle;
    private readonly InteractionReducer _interaction = new();
    private readonly IClipboard _clipboard;
    private readonly Queue<PendingEvent> _pendingEvents = new();
    private readonly object _pendingEventsLock = new();
    private readonly JobScheduler _jobScheduler;
    private readonly CommandDispatcher _commandDispatcher;
    private readonly BlockCommandRegistry _blockCommands;
    private readonly InputPreprocessorRegistry _inputPreprocessors = new();
    private readonly Dictionary<JobId, JobProjection> _jobProjections = new();
    private readonly string _defaultPromptText;
    private long _nextPromptId;
    private readonly Dictionary<BlockId, JobPromptRef> _jobPromptRefs = new();
    private readonly Dictionary<JobId, BlockId> _activeJobPromptBlocks = new();
    private long _nextOwnedPromptId;
    private readonly Dictionary<BlockId, OwnedPromptRef> _ownedPromptRefs = new();
    private readonly Dictionary<BlockId, BlockId> _commandIndicators = new();
    private readonly Dictionary<BlockId, long> _commandIndicatorStartTicks = new();
    private readonly Dictionary<BlockId, BlockId> _visibleCommandIndicators = new();
    private bool _pendingShellPrompt;
    private readonly Dictionary<(JobId JobId, TextStream Stream), ChunkAccumulator> _chunkAccumulators = new();
    private readonly List<string> _commandHistory = new();
    private int _historyIndex;
    private string _historyDraft = string.Empty;
    private bool _pendingExit;

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
    private bool _pendingContentRebuild;
    private byte _lastCaretAlpha = 0xFF;
    private long _lastCaretRenderTicks;
    private byte _lastScrollbarTrackAlpha;
    private byte _lastScrollbarThumbAlpha;
    private int _lastSpinnerFrameIndex = -1;
    private long _lastTickTicks;
    private bool _scrollbarInteractedThisTick;
    private Scene3DCapture? _scene3DCapture;

    private ConsoleHostSession(
        string text,
        IClipboard clipboard,
        int resizeSettleMs,
        int rebuildThrottleMs,
        Action<BlockCommandRegistry>? configureBlockCommands)
    {
        _resizeSettleMs = resizeSettleMs;
        _rebuildThrottleMs = rebuildThrottleMs;

        _clipboard = clipboard;
        _document = CreateDocument(text);
        _interaction.Initialize(_document.Transcript);
        SetCaretToEndOfLastPrompt(_document.Transcript);
        _defaultPromptText = FindLastPrompt(_document.Transcript)?.Prompt ?? "> ";
        _layoutSettings = new LayoutSettings();
        var fontService = new FontService();
        _font = fontService.CreateDefaultFont(_layoutSettings);
        _layoutSettings.PaddingPolicy = PaddingPolicy.None;
        _layoutSettings.BorderLeftRightPx = 5;
        _layoutSettings.BorderTopBottomPx = 3;

        _atlasData = _font.Atlas;
        _renderer = new ConsoleRenderer();
        _selectionStyle = SelectionStyle.Default;
        _layoutEngine = new LayoutEngine();

        _jobScheduler = new JobScheduler();
        var registry = new CommandRegistry();
        registry.Register(new EchoCommandHandler());
        registry.Register(new AskCommandHandler());
        _commandDispatcher = new CommandDispatcher(
            registry,
            _jobScheduler,
            EmptyServiceProvider.Instance,
            cwdProvider: () => Directory.GetCurrentDirectory(),
            envProvider: static () => new Dictionary<string, string>());

        _blockCommands = new BlockCommandRegistry();
        _blockCommands.Register(new EchoBlockCommandHandler());
        _blockCommands.Register(new AskBlockCommandHandler());
        _blockCommands.Register(new ClearBlockCommandHandler());
        _blockCommands.Register(new ExitBlockCommandHandler());
        _blockCommands.Register(new WaitBlockCommandHandler());
        _blockCommands.Register(new ProgressBlockCommandHandler());
        configureBlockCommands?.Invoke(_blockCommands);
    }

    public static ConsoleHostSession CreateVga(
        string text,
        IClipboard clipboard,
        int resizeSettleMs = 80,
        int rebuildThrottleMs = 80,
        Action<BlockCommandRegistry>? configureBlockCommands = null)
    {
        return new ConsoleHostSession(text, clipboard, resizeSettleMs, rebuildThrottleMs, configureBlockCommands);
    }

    public GlyphAtlasData Atlas => _atlasData;

    public void OnTextInput(HostTextInputEvent e)
    {
        lock (_pendingEventsLock)
        {
            _pendingEvents.Enqueue(new PendingEvent.Text(e.Ch));
        }
    }

    public void OnKeyEvent(HostKeyEvent e)
    {
        lock (_pendingEventsLock)
        {
            _pendingEvents.Enqueue(new PendingEvent.Key(e.Key, e.Mods, e.IsDown));
        }
    }

    public void OnMouseEvent(HostMouseEvent e)
    {
        lock (_pendingEventsLock)
        {
            _pendingEvents.Enqueue(new PendingEvent.Mouse(e));
        }
    }

    public void OnFileDrop(HostFileDropEvent e)
    {
        lock (_pendingEventsLock)
        {
            _pendingEvents.Enqueue(new PendingEvent.FileDrop(e.Path));
        }
    }

    public void ReportRenderFailure(int blockId, string reason)
    {
        var id = new BlockId(blockId);
        var blocks = _document.Transcript.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id != id)
            {
                continue;
            }

            _document.Transcript.ReplaceAt(i, new TextBlock(id, $"Render failed: {reason}", ConsoleTextStream.System));
            _pendingContentRebuild = true;
            return;
        }
    }

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
        _scrollbarInteractedThisTick = false;
        var dtMs = _lastTickTicks == 0
            ? 0
            : (int)Math.Clamp((nowTicks - _lastTickTicks) * 1000.0 / Stopwatch.Frequency, 0, 250);
        _lastTickTicks = nowTicks;

        if (TickRunnableBlocks(TimeSpan.FromMilliseconds(dtMs)))
        {
            _pendingContentRebuild = true;
        }

        UpdateVisibleCommandIndicators(nowTicks);
        EnsureShellPromptAtEndIfNeeded();

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

        var caretAlphaNow = ComputeCaretAlpha(nowTicks);
        var timeSeconds = nowTicks / (double)Stopwatch.Frequency;

        var renderedGrid = _lastFrame?.BuiltGrid ?? default;

        var gridMismatch = _lastFrame is null || renderedGrid != currentGrid;
        if (gridMismatch) _pendingResizeRebuild = true;

        var elapsedSinceRebuildMs = (nowTicks - _lastRebuildTicks) * 1000.0 / Stopwatch.Frequency;
        var shouldRebuild = _lastFrame is null
            || _pendingContentRebuild
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
                    restoreAnchor,
                    caretAlphaNow,
                    timeSeconds);

                _lastFrame = frame;
                _renderedGrid = builtGrid;
                _lastLayout = layout;
                LogOnce(_atlasData, layout, renderFrame, snapW, snapH);
                _lastRebuildTicks = passNowTicks;
                _lastCaretAlpha = caretAlphaNow;
                var (trackAlpha, thumbAlpha) = ComputeScrollbarAlphas();
                _lastScrollbarTrackAlpha = trackAlpha;
                _lastScrollbarThumbAlpha = thumbAlpha;
                _lastSpinnerFrameIndex = ComputeSpinnerFrameIndex(passNowTicks);
                _lastCaretRenderTicks = passNowTicks;

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
            _pendingContentRebuild = false;

            framebufferWidth = snapW;
            framebufferHeight = snapH;
            currentGrid = snapGrid;
        }

        DrainPendingEvents(framebufferWidth, framebufferHeight);

        DrainJobEvents();

        AdvanceScrollbarAnimation(dtMs);

        if (_pendingContentRebuild)
        {
            var (frame, builtGrid, layout, renderFrame) = BuildFrameFor(
                framebufferWidth,
                framebufferHeight,
                restoreAnchor: false,
                caretAlpha: caretAlphaNow,
                timeSeconds: timeSeconds);

            _lastFrame = frame;
            _renderedGrid = builtGrid;
            _lastLayout = layout;
            LogOnce(_atlasData, layout, renderFrame, framebufferWidth, framebufferHeight);
            _lastRebuildTicks = Stopwatch.GetTimestamp();
            _lastCaretAlpha = caretAlphaNow;
            var (trackAlpha, thumbAlpha) = ComputeScrollbarAlphas();
            _lastScrollbarTrackAlpha = trackAlpha;
            _lastScrollbarThumbAlpha = thumbAlpha;
            _lastSpinnerFrameIndex = ComputeSpinnerFrameIndex(_lastRebuildTicks);
            _lastCaretRenderTicks = _lastRebuildTicks;
            _pendingContentRebuild = false;
        }

        MaybeUpdateOverlays(framebufferWidth, framebufferHeight, nowTicks, framebufferSettled);

        if (_lastFrame is null)
        {
            throw new InvalidOperationException("Tick invariant violated: frame must be available.");
        }

        var setVSync = _pendingSetVSync;
        _pendingSetVSync = null;
        var requestExit = _pendingExit;
        _pendingExit = false;
        return new FrameTickResult(framebufferWidth, framebufferHeight, _lastFrame, setVSync, requestExit);
    }

    private void MaybeUpdateOverlays(int framebufferWidth, int framebufferHeight, long nowTicks, bool framebufferSettled)
    {
        if (!framebufferSettled || _lastLayout is null)
        {
            return;
        }

        UpdateVisibleCommandIndicators(nowTicks);
        var spinnerIndex = ComputeSpinnerFrameIndex(nowTicks);
        var minIntervalMs = 33;
        if (spinnerIndex != -1)
        {
            var indicators = _document.Settings.Indicators;
            var fps = Math.Max(1, indicators.AnimationFps);
            minIntervalMs = (int)Math.Clamp(Math.Floor(1000.0 / fps), 1.0, 33.0);
        }

        var elapsedSinceCaretMs = (nowTicks - _lastCaretRenderTicks) * 1000.0 / Stopwatch.Frequency;
        if (_lastCaretRenderTicks != 0 && elapsedSinceCaretMs < minIntervalMs)
        {
            return;
        }

        var caretAlpha = ComputeCaretAlpha(nowTicks);
        var (trackAlpha, thumbAlpha) = ComputeScrollbarAlphas();
        if (caretAlpha == _lastCaretAlpha &&
            trackAlpha == _lastScrollbarTrackAlpha &&
            thumbAlpha == _lastScrollbarThumbAlpha &&
            spinnerIndex == _lastSpinnerFrameIndex)
        {
            return;
        }

        var timeSeconds = nowTicks / (double)Stopwatch.Frequency;
        var renderFrame = _renderer.Render(_document, _lastLayout, _font, _selectionStyle, timeSeconds: timeSeconds, commandIndicators: _visibleCommandIndicators, caretAlpha: caretAlpha);
        var backendFrame = RenderFrameAdapter.Adapt(renderFrame);
        _lastFrame = backendFrame;
        _renderedGrid = backendFrame.BuiltGrid;
        _lastCaretAlpha = caretAlpha;
        _lastScrollbarTrackAlpha = trackAlpha;
        _lastScrollbarThumbAlpha = thumbAlpha;
        _lastSpinnerFrameIndex = spinnerIndex;
        _lastCaretRenderTicks = nowTicks;
    }

    private void UpdateVisibleCommandIndicators(long nowTicks)
    {
        _visibleCommandIndicators.Clear();

        if (_commandIndicators.Count == 0)
        {
            _commandIndicatorStartTicks.Clear();
            return;
        }

        var delaySeconds = Math.Max(0.0, _document.Settings.Indicators.ShowDelaySeconds);
        var delayTicks = (long)Math.Round(delaySeconds * Stopwatch.Frequency);

        List<BlockId>? removeKeys = null;
        foreach (var kvp in _commandIndicators)
        {
            var commandEchoId = kvp.Key;
            var activityBlockId = kvp.Value;

            if (!TryGetBlock(commandEchoId, out var commandBlock) || commandBlock is not TextBlock)
            {
                removeKeys ??= new List<BlockId>();
                removeKeys.Add(commandEchoId);
                continue;
            }

            if (!TryGetBlock(activityBlockId, out var activityBlock) || activityBlock is not IRunnableBlock runnable)
            {
                removeKeys ??= new List<BlockId>();
                removeKeys.Add(commandEchoId);
                continue;
            }

            if (runnable.State != BlockRunState.Running)
            {
                removeKeys ??= new List<BlockId>();
                removeKeys.Add(commandEchoId);
                continue;
            }

            if (!_commandIndicatorStartTicks.TryGetValue(commandEchoId, out var startTicks))
            {
                startTicks = nowTicks;
                _commandIndicatorStartTicks[commandEchoId] = startTicks;
            }

            if (nowTicks - startTicks >= delayTicks)
            {
                _visibleCommandIndicators[commandEchoId] = activityBlockId;
            }
        }

        if (removeKeys is null)
        {
            return;
        }

        foreach (var key in removeKeys)
        {
            _commandIndicators.Remove(key);
            _commandIndicatorStartTicks.Remove(key);
        }
    }

    private int ComputeSpinnerFrameIndex(long nowTicks)
    {
        if (!HasIndeterminateSpinner())
        {
            return -1;
        }

        var indicators = _document.Settings.Indicators;
        var fps = Math.Max(1, indicators.AnimationFps);
        var timeSeconds = nowTicks / (double)Stopwatch.Frequency;
        return (int)Math.Floor(timeSeconds * fps);
    }

    private bool HasIndeterminateSpinner()
    {
        if (_visibleCommandIndicators.Count == 0)
        {
            return false;
        }

        // Any visible indicator implies we need overlay animation ticks (pulse + progress fill updates).
        return true;
    }

    private static byte ComputeCaretAlpha(long nowTicks)
    {
        // Fade-in/out blink (gimmicky but readable): off → fade in → on → fade out → off.
        const double periodSeconds = 1.2;
        var t = nowTicks / (double)Stopwatch.Frequency;
        var phase = t % periodSeconds;
        var p = phase / periodSeconds; // 0..1

        static double SmoothStep(double x)
        {
            x = Math.Clamp(x, 0.0, 1.0);
            return x * x * (3.0 - 2.0 * x);
        }

        double a;
        if (p < 0.15)
        {
            a = 0.0;
        }
        else if (p < 0.25)
        {
            a = SmoothStep((p - 0.15) / 0.10);
        }
        else if (p < 0.65)
        {
            a = 1.0;
        }
        else if (p < 0.75)
        {
            a = 1.0 - SmoothStep((p - 0.65) / 0.10);
        }
        else
        {
            a = 0.0;
        }

        return (byte)Math.Clamp((int)Math.Round(a * 255.0), 0, 255);
    }

    private void AdvanceScrollbarAnimation(int dtMs)
    {
        var ui = _document.Scroll.ScrollbarUi;
        if (_scrollbarInteractedThisTick)
        {
            ui.MsSinceInteraction = 0;
        }
        else if (dtMs > 0)
        {
            ui.MsSinceInteraction = (int)Math.Min(int.MaxValue, (long)ui.MsSinceInteraction + dtMs);
        }

        var settings = _document.Settings.Scrollbar;
        var wantsVisible = ui.IsDragging || ui.IsHovering || ui.MsSinceInteraction < settings.AutoHideDelayMs;
        var target = wantsVisible ? 1f : 0f;
        var fadeMs = wantsVisible ? settings.FadeInMs : settings.FadeOutMs;

        if (fadeMs <= 0)
        {
            ui.Visibility = target;
            return;
        }

        var step = dtMs / (float)fadeMs;
        if (step <= 0f)
        {
            return;
        }

        ui.Visibility = MoveTowards(ui.Visibility, target, step);
    }

    private static float MoveTowards(float value, float target, float maxDelta)
    {
        if (value < target)
        {
            return Math.Min(value + maxDelta, target);
        }

        if (value > target)
        {
            return Math.Max(value - maxDelta, target);
        }

        return value;
    }

    private (byte TrackAlpha, byte ThumbAlpha) ComputeScrollbarAlphas()
    {
        var ui = _document.Scroll.ScrollbarUi;
        var settings = _document.Settings.Scrollbar;
        var visibility = Math.Clamp(ui.Visibility, 0f, 1f);

        var thumbOpacity = ui.IsDragging
            ? settings.ThumbOpacityDrag
            : ui.IsHovering
                ? settings.ThumbOpacityHover
                : settings.ThumbOpacityIdle;

        return (ToAlpha(visibility * settings.TrackOpacityIdle), ToAlpha(visibility * thumbOpacity));
    }

    private static byte ToAlpha(float alpha01)
    {
        alpha01 = Math.Clamp(alpha01, 0f, 1f);
        return (byte)Math.Clamp((int)Math.Round(alpha01 * 255f), 0, 255);
    }

    private void DrainPendingEvents(int framebufferWidth, int framebufferHeight)
    {
        List<PendingEvent> events;
        lock (_pendingEventsLock)
        {
            if (_pendingEvents.Count == 0)
            {
                return;
            }

            events = new List<PendingEvent>(_pendingEvents.Count);
            while (_pendingEvents.Count > 0)
            {
                events.Add(_pendingEvents.Dequeue());
            }
        }

        EnsureLayoutExists(framebufferWidth, framebufferHeight);

        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is PendingEvent.FileDrop fileDrop)
            {
                HandleFileDrop(fileDrop.Path);
                EnsureLayoutExists(framebufferWidth, framebufferHeight);
                continue;
            }

            if (events[i] is PendingEvent.Mouse mouseRaw && _lastLayout is not null)
            {
                var sceneConsumed = HandleScene3DMouse(mouseRaw.Event);
                if (sceneConsumed)
                {
                    continue;
                }

                var consumed = HandleScrollbarMouse(mouseRaw.Event);
                if (consumed)
                {
                    continue;
                }
            }

            var ev = Translate(events[i]);
            if (ev is null || _lastLayout is null)
            {
                continue;
            }

            var actions = _interaction.Handle(ev, _lastLayout, _document.Transcript);
            ApplyActions(actions);

            if (_pendingContentRebuild &&
                i + 1 < events.Count &&
                events[i + 1] is PendingEvent.Mouse)
            {
                EnsureLayoutExists(framebufferWidth, framebufferHeight);
                _pendingContentRebuild = false;
            }
        }
    }

    private void EnsureLayoutExists(int framebufferWidth, int framebufferHeight)
    {
        if (_lastLayout is not null && _lastFrame is not null && !_pendingContentRebuild)
        {
            return;
        }

        var nowTicks = Stopwatch.GetTimestamp();
        UpdateVisibleCommandIndicators(nowTicks);
        var caretAlphaNow = ComputeCaretAlpha(nowTicks);
        var timeSeconds = nowTicks / (double)Stopwatch.Frequency;
        var (frame, builtGrid, layout, renderFrame) = BuildFrameFor(
            framebufferWidth,
            framebufferHeight,
            restoreAnchor: false,
            caretAlpha: caretAlphaNow,
            timeSeconds: timeSeconds);

        _lastFrame = frame;
        _renderedGrid = builtGrid;
        _lastLayout = layout;
        LogOnce(_atlasData, layout, renderFrame, framebufferWidth, framebufferHeight);
        _lastRebuildTicks = nowTicks;
        _lastCaretAlpha = caretAlphaNow;
        var (trackAlpha, thumbAlpha) = ComputeScrollbarAlphas();
        _lastScrollbarTrackAlpha = trackAlpha;
        _lastScrollbarThumbAlpha = thumbAlpha;
        _lastSpinnerFrameIndex = ComputeSpinnerFrameIndex(nowTicks);
        _lastCaretRenderTicks = nowTicks;
    }

    private InputEvent? Translate(PendingEvent e)
    {
        return e switch
        {
            PendingEvent.Text text => new InputEvent.Text(text.Ch),
            PendingEvent.Key key => key.IsDown
                ? new InputEvent.KeyDown(key.KeyCode, key.Mods)
                : new InputEvent.KeyUp(key.KeyCode, key.Mods),
            PendingEvent.Mouse mouse => TranslateMouse(mouse.Event),
            _ => null
        };
    }

    private InputEvent? TranslateMouse(HostMouseEvent e)
    {
        if (_lastLayout is null)
        {
            return null;
        }

        var scrollOffsetRows = GetScrollOffsetRows(_document, _lastLayout);
        var adjustedX = e.X;
        var adjustedY = e.Y + (scrollOffsetRows * _font.Metrics.CellHeightPx);

        return e.Kind switch
        {
            HostMouseEventKind.Down when (e.Buttons & HostMouseButtons.Left) != 0 =>
                new InputEvent.MouseDown(adjustedX, adjustedY, MouseButton.Left, e.Mods),
            HostMouseEventKind.Move =>
                new InputEvent.MouseMove(adjustedX, adjustedY, e.Buttons, e.Mods),
            HostMouseEventKind.Up when (e.Buttons & HostMouseButtons.Left) != 0 =>
                new InputEvent.MouseUp(adjustedX, adjustedY, MouseButton.Left, e.Mods),
            HostMouseEventKind.Wheel => null,
            _ => null
        };
    }

    private bool HandleScene3DMouse(HostMouseEvent e)
    {
        if (_lastLayout is null)
        {
            return false;
        }

        var viewports = _lastLayout.Scene3DViewports;
        if (viewports.Count == 0 && _scene3DCapture is null)
        {
            return false;
        }

        var scrollOffsetRows = GetScrollOffsetRows(_document, _lastLayout);
        var contentX = e.X;
        var contentY = e.Y + (scrollOffsetRows * _font.Metrics.CellHeightPx);

        if (_scene3DCapture is { } capture)
        {
            if (!TryGetBlock(capture.BlockId, out var block) || block is not IScene3DViewBlock stl)
            {
                _scene3DCapture = null;
                return false;
            }

            switch (e.Kind)
            {
                case HostMouseEventKind.Move:
                    ApplySceneDrag(stl, _document.Settings.Scene3D, capture.Mode, capture.LastX, capture.LastY, contentX, contentY);
                    _scene3DCapture = capture with { LastX = contentX, LastY = contentY };
                    _pendingContentRebuild = true;
                    return true;
                case HostMouseEventKind.Up when (e.Buttons & capture.Button) != 0:
                    _scene3DCapture = null;
                    return true;
                case HostMouseEventKind.Wheel:
                    return true;
            }

            return true;
        }

        var hit = FindSceneViewportAt(contentX, contentY, viewports);
        if (hit is null)
        {
            return false;
        }

        if (!TryGetBlock(hit.Value.BlockId, out var hitBlock) || hitBlock is not IScene3DViewBlock hitStl)
        {
            return false;
        }

        if (e.Kind == HostMouseEventKind.Wheel)
        {
            ApplySceneZoom(hitStl, _document.Settings.Scene3D, e.WheelDelta);
            _pendingContentRebuild = true;
            return true;
        }

        if (e.Kind == HostMouseEventKind.Down)
        {
            if ((e.Buttons & HostMouseButtons.Left) != 0)
            {
                _scene3DCapture = new Scene3DCapture(hit.Value.BlockId, HostMouseButtons.Left, Scene3DDragMode.Orbit, contentX, contentY);
                return true;
            }

            if ((e.Buttons & HostMouseButtons.Right) != 0)
            {
                _scene3DCapture = new Scene3DCapture(hit.Value.BlockId, HostMouseButtons.Right, Scene3DDragMode.Pan, contentX, contentY);
                return true;
            }
        }

        return false;
    }

    private Scene3DViewportLayout? FindSceneViewportAt(int x, int y, IReadOnlyList<Scene3DViewportLayout> viewports)
    {
        for (var i = 0; i < viewports.Count; i++)
        {
            var viewport = viewports[i];
            if (viewport.ViewportRectPx.Contains(x, y))
            {
                return viewport;
            }
        }

        return null;
    }

    private static void ApplySceneDrag(IScene3DViewBlock stl, Scene3DSettings settings, Scene3DDragMode mode, int lastX, int lastY, int x, int y)
    {
        var dx = x - lastX;
        var dy = y - lastY;

        switch (mode)
        {
            case Scene3DDragMode.Orbit:
            {
                stl.YawRadians += dx * settings.OrbitSensitivity * (settings.InvertOrbitX ? -1f : 1f);
                stl.PitchRadians += dy * settings.OrbitSensitivity * (settings.InvertOrbitY ? -1f : 1f);
                stl.PitchRadians = Math.Clamp(stl.PitchRadians, -1.55f, 1.55f);
                break;
            }
            case Scene3DDragMode.Pan:
            {
                var forward = ComputeForward(stl.YawRadians, stl.PitchRadians);
                var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                if (right.LengthSquared() < 1e-6f)
                {
                    right = Vector3.UnitX;
                }

                var up = Vector3.Normalize(Vector3.Cross(right, forward));
                var scale = Math.Max(0.01f, stl.Distance) * settings.PanSensitivity;
                stl.Target += (dx * scale * (settings.InvertPanX ? -1f : 1f)) * right;
                stl.Target += (dy * scale * (settings.InvertPanY ? -1f : 1f)) * up;
                break;
            }
        }
    }

    private static void ApplySceneZoom(IScene3DViewBlock stl, Scene3DSettings settings, int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        var delta = settings.InvertZoom ? -wheelDelta : wheelDelta;
        var factor = MathF.Exp(-delta * settings.ZoomSensitivity);
        var maxDistance = MathF.Max(0.01f, stl.BoundsRadius * 100f);
        stl.Distance = Math.Clamp(stl.Distance * factor, 0.01f, maxDistance);
    }

    private static Vector3 ComputeForward(float yaw, float pitch)
    {
        var cy = MathF.Cos(yaw);
        var sy = MathF.Sin(yaw);
        var cp = MathF.Cos(pitch);
        var sp = MathF.Sin(pitch);
        var forward = new Vector3(sy * cp, sp, cy * cp);
        if (forward.LengthSquared() < 1e-10f)
        {
            return new Vector3(0, 0, 1);
        }

        return Vector3.Normalize(forward);
    }

    private static ConsoleDocument CreateDocument(string text)
    {
        var transcript = new Transcript();
        foreach (var line in SplitLines(text))
        {
            transcript.Add(new TextBlock(new BlockId(transcript.Blocks.Count + 1), line));
        }

        transcript.Add(new PromptBlock(new BlockId(transcript.Blocks.Count + 1), "> "));

        return new ConsoleDocument(
            transcript,
            new InputState(),
            new ScrollState(),
            new SelectionState(),
            new ConsoleSettings());
    }

    private static string[] SplitLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);
    }

    private static void SetCaretToEndOfLastPrompt(Transcript transcript)
    {
        var prompt = FindLastPrompt(transcript);
        if (prompt is null)
        {
            return;
        }

        prompt.SetCaret(prompt.Input.Length);
    }

    private void ApplyActions(IReadOnlyList<HostAction> actions)
    {
        if (actions.Count == 0)
        {
            _document.Selection.ActiveRange = _interaction.Snapshot.Selection;
            return;
        }

        foreach (var action in actions)
        {
            switch (action)
            {
                case HostAction.Focus:
                case HostAction.SetMouseCapture:
                    break;
                case HostAction.ClearSelection:
                    _document.Selection.ActiveRange = null;
                    break;
                case HostAction.InsertText insert:
                    if (TryGetPrompt(insert.PromptId, out var insertPrompt))
                    {
                        insertPrompt.InsertText(insert.Text);
                        _document.Scroll.IsFollowingTail = true;
                    }
                    break;
                case HostAction.Backspace backspace:
                    if (TryGetPrompt(backspace.PromptId, out var backspacePrompt))
                    {
                        backspacePrompt.Backspace();
                        _document.Scroll.IsFollowingTail = true;
                    }
                    break;
                case HostAction.MoveCaret moveCaret:
                    if (TryGetPrompt(moveCaret.PromptId, out var moveCaretPrompt))
                    {
                        moveCaretPrompt.MoveCaret(moveCaret.Delta);
                    }
                    break;
                case HostAction.SetCaret setCaret:
                    if (TryGetPrompt(setCaret.PromptId, out var setCaretPrompt))
                    {
                        setCaretPrompt.SetCaret(setCaret.Index);
                    }
                    break;
                case HostAction.SubmitPrompt submit:
                    CommitPromptIfAny(submit.PromptId);
                    _document.Scroll.IsFollowingTail = true;
                    break;
                case HostAction.NavigateHistory nav:
                    NavigateHistory(nav.PromptId, nav.Delta);
                    break;
                case HostAction.CopySelectionToClipboard:
                    if (_interaction.TryGetSelectedText(_document.Transcript, out var selected))
                    {
                        _clipboard.SetText(selected);
                    }
                    break;
                case HostAction.PasteFromClipboardIntoLastPrompt:
                    PasteIntoLastPrompt();
                    break;
                case HostAction.StopFocusedBlock:
                    StopFocusedBlock(null);
                    break;
                case HostAction.StopFocusedBlockWithLevel stopWithLevel:
                    StopFocusedBlock(stopWithLevel.Level);
                    break;
                case HostAction.RequestRebuild:
                    _pendingContentRebuild = true;
                    break;
            }
        }

        _document.Selection.ActiveRange = _interaction.Snapshot.Selection;
    }

    private void StopFocusedBlock(StopLevel? levelOverride)
    {
        var focused = _interaction.Snapshot.Focused;
        if (focused is null)
        {
            return;
        }

        if (!TryGetBlock(focused.Value, out var block))
        {
            return;
        }

        if (block is not IStoppableBlock stoppable || !stoppable.CanStop)
        {
            return;
        }

        var level = levelOverride ?? StopLevel.Soft;
        stoppable.RequestStop(level);
        UpdateVisibleCommandIndicators(Stopwatch.GetTimestamp());

        _document.Scroll.IsFollowingTail = true;

        if (_pendingShellPrompt)
        {
            EnsureShellPromptAtEndIfNeeded();
        }

        if (block is PromptBlock { Owner: not null } && _ownedPromptRefs.ContainsKey(block.Id))
        {
            if (TryGetPrompt(block.Id, out var ownedPrompt))
            {
                ReplacePromptWithArchivedText(block.Id, ownedPrompt.Prompt + ownedPrompt.Input, ConsoleTextStream.Default);
            }

            _ownedPromptRefs.Remove(block.Id);
            _pendingShellPrompt = true;
            EnsureShellPromptAtEndIfNeeded();
        }

        if (block is PromptBlock { Owner: not null } && _jobPromptRefs.TryGetValue(block.Id, out var jobPrompt))
        {
            if (_jobScheduler.TryGetJob(jobPrompt.JobId, out var job))
            {
                var cancelLevel = MapStopLevel(level);
                _ = job.RequestCancelAsync(cancelLevel, CancellationToken.None);
            }
        }

        _pendingContentRebuild = true;
    }

    private static CancelLevel MapStopLevel(StopLevel level) =>
        level switch
        {
            StopLevel.Soft => CancelLevel.Soft,
            StopLevel.Terminate => CancelLevel.Terminate,
            StopLevel.Kill => CancelLevel.Kill,
            _ => CancelLevel.Soft
        };

    private void PasteIntoLastPrompt()
    {
        var paste = _clipboard.GetText();
        if (string.IsNullOrEmpty(paste))
        {
            return;
        }

        var promptId = _interaction.Snapshot.LastPromptId;
        if (promptId is null || !TryGetPrompt(promptId.Value, out var prompt))
        {
            return;
        }

        prompt.InsertText(paste);
        _document.Scroll.IsFollowingTail = true;
    }

    private void CommitPromptIfAny(BlockId promptId)
    {
        if (!TryGetPrompt(promptId, out var prompt))
        {
            return;
        }

        var command = prompt.Input;

        if (_ownedPromptRefs.ContainsKey(promptId))
        {
            var promptLine = prompt.Prompt + command;
            ReplacePromptWithArchivedText(promptId, promptLine, ConsoleTextStream.Default);

            _ownedPromptRefs.Remove(promptId);
            _pendingShellPrompt = true;
            EnsureShellPromptAtEndIfNeeded();

            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
            return;
        }

        if (_jobPromptRefs.TryGetValue(promptId, out var jobPrompt))
        {
            var promptLine = prompt.Prompt + command;
            ReplacePromptWithArchivedText(promptId, promptLine, ConsoleTextStream.Default);

            if (_jobScheduler.TryGetJob(jobPrompt.JobId, out var job))
            {
                _ = job.SendInputAsync(command, CancellationToken.None);
            }
            else
            {
                AppendJobText(jobPrompt.JobId, TextStream.System, "Interactive job is no longer running.");
            }

            _jobPromptRefs.Remove(promptId);
            _activeJobPromptBlocks.Remove(jobPrompt.JobId);
            _pendingShellPrompt = true;

            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
        }
        else
        {
            var insertIndex = Math.Max(0, _document.Transcript.Blocks.Count - 1);
            var headerId = new BlockId(AllocateNewBlockId());
            _document.Transcript.Insert(insertIndex, new TextBlock(headerId, prompt.Prompt + command));

            RecordCommandHistory(command);

            var commandForParse = command;
            if (_inputPreprocessors.TryRewrite(command, out var rewritten))
            {
                commandForParse = rewritten;
            }

            var request = CommandLineParser.Parse(commandForParse);
            if (request is null)
            {
                prompt.Input = string.Empty;
                prompt.SetCaret(0);
                _historyIndex = _commandHistory.Count;
                _historyDraft = string.Empty;
                return;
            }

            var ctx = new BlockCommandContext(this, headerId, promptId);
            if (_blockCommands.TryExecute(request, ctx))
            {
                _document.Scroll.IsFollowingTail = true;
                _pendingContentRebuild = true;

                if (ctx.StartedBlockingActivity)
                {
                    RemoveShellPromptIfPresent(promptId);
                    _pendingShellPrompt = true;
                }
            }
            else if (!string.IsNullOrWhiteSpace(command))
            {
                _document.Transcript.Insert(insertIndex + 1, new TextBlock(new BlockId(AllocateNewBlockId()), "Unrecognized command."));
                _document.Scroll.IsFollowingTail = true;
                _pendingContentRebuild = true;
            }

            prompt.Input = string.Empty;
            prompt.SetCaret(0);
            _historyIndex = _commandHistory.Count;
            _historyDraft = string.Empty;
        }
    }

    private void HandleFileDrop(string path)
    {
        var blocks = _document.Transcript.Blocks;
        if (blocks.Count == 0)
        {
            return;
        }

        var commandText = $"deconstruct {QuoteForCommandLineParser(path)}";

        var insertIndex = Math.Max(0, blocks.Count - 1);
        var headerId = new BlockId(AllocateNewBlockId());
        _document.Transcript.Insert(insertIndex, new TextBlock(headerId, _defaultPromptText + commandText, ConsoleTextStream.Default));

        RecordCommandHistory(commandText);

        var commandForParse = commandText;
        if (_inputPreprocessors.TryRewrite(commandText, out var rewritten))
        {
            commandForParse = rewritten;
        }

        var request = CommandLineParser.Parse(commandForParse);
        if (request is null)
        {
            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
            return;
        }

        var shellPromptId = FindLastPrompt(_document.Transcript)?.Id ?? headerId;
        var ctx = new BlockCommandContext(this, headerId, shellPromptId);
        if (_blockCommands.TryExecute(request, ctx))
        {
            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
            return;
        }

        InsertBlockAfter(headerId, new TextBlock(new BlockId(AllocateNewBlockId()), "Unrecognized command.", ConsoleTextStream.System));
        _document.Scroll.IsFollowingTail = true;
        _pendingContentRebuild = true;
    }

    private static string QuoteForCommandLineParser(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private void RecordCommandHistory(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return;
        }

        if (_commandHistory.Count == 0 || !string.Equals(_commandHistory[^1], commandText, StringComparison.Ordinal))
        {
            _commandHistory.Add(commandText);
        }

        _historyIndex = _commandHistory.Count;
        _historyDraft = string.Empty;
    }

    private void AppendOwnedPrompt(string promptText)
    {
        var blocks = _document.Transcript.Blocks;
        if (blocks.Count == 0)
        {
            return;
        }

        var lastIndex = blocks.Count - 1;
        if (blocks[lastIndex] is PromptBlock existingShellPrompt && existingShellPrompt.Owner is null)
        {
            if (string.IsNullOrEmpty(existingShellPrompt.Input))
            {
                _document.Transcript.RemoveAt(lastIndex);
            }
            else
            {
                var archivedText = existingShellPrompt.Prompt + existingShellPrompt.Input;
                var archived = new TextBlock(existingShellPrompt.Id, archivedText, ConsoleTextStream.Default);
                _document.Transcript.ReplaceAt(lastIndex, archived);
            }
        }

        var promptId = Interlocked.Increment(ref _nextOwnedPromptId);
        var promptBlockId = new BlockId(AllocateNewBlockId());
        var block = new PromptBlock(promptBlockId, promptText, new PromptOwner(OwnerId: 1, PromptId: promptId))
        {
            Input = string.Empty,
            CaretIndex = 0
        };

        _document.Transcript.Add(block);
        _ownedPromptRefs[promptBlockId] = new OwnedPromptRef(promptId);
        _pendingShellPrompt = true;
    }

    private sealed class BlockCommandContext : IBlockCommandContext
    {
        private readonly ConsoleHostSession _session;
        private readonly BlockId _commandEchoId;
        private readonly BlockId _shellPromptId;
        private BlockId _insertAfterId;
        private bool _startedBlockingActivity;

        public BlockCommandContext(ConsoleHostSession session, BlockId commandEchoId, BlockId shellPromptId)
        {
            _session = session;
            _commandEchoId = commandEchoId;
            _shellPromptId = shellPromptId;
            _insertAfterId = commandEchoId;
        }

        public bool StartedBlockingActivity => _startedBlockingActivity;

        public BlockId CommandEchoId => _commandEchoId;

        public void InsertTextAfterCommandEcho(string text, ConsoleTextStream stream)
        {
            var id = new BlockId(_session.AllocateNewBlockId());
            _session.InsertBlockAfter(_insertAfterId, new TextBlock(id, text, stream));
            _insertAfterId = id;
        }

        public BlockId AllocateBlockId() => new(_session.AllocateNewBlockId());

        public void InsertBlockAfterCommandEcho(IBlock block)
        {
            _session.InsertBlockAfter(_insertAfterId, block);
            _insertAfterId = block.Id;

            if (block is IRunnableBlock runnable &&
                runnable.State == BlockRunState.Running &&
                block is not PromptBlock)
            {
                _startedBlockingActivity = true;
            }
        }

        public void AttachIndicator(BlockId activityBlockId)
        {
            _session._commandIndicators[_commandEchoId] = activityBlockId;
            _session._pendingContentRebuild = true;
        }

        public void AppendOwnedPrompt(string promptText)
        {
            if (_session.TryGetPrompt(_shellPromptId, out var prompt))
            {
                prompt.Input = string.Empty;
                prompt.SetCaret(0);
            }

            _session.AppendOwnedPrompt(promptText);
        }

        public void ClearTranscript() => _session.ClearTranscript();

        public void RequestExit()
        {
            _session._pendingExit = true;
            _session._pendingContentRebuild = true;
        }
    }

    private void NavigateHistory(BlockId promptId, int delta)
    {
        if (delta == 0 || !TryGetPrompt(promptId, out var prompt))
        {
            return;
        }

        if (prompt.Owner is not null)
        {
            return;
        }

        if (_commandHistory.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Clamp(_historyIndex, 0, _commandHistory.Count);

        if (delta < 0)
        {
            if (_historyIndex == _commandHistory.Count)
            {
                _historyDraft = prompt.Input;
            }

            if (_historyIndex == 0)
            {
                return;
            }

            _historyIndex--;
            prompt.Input = _commandHistory[_historyIndex];
            prompt.SetCaret(prompt.Input.Length);
            return;
        }

        if (delta > 0)
        {
            if (_historyIndex == _commandHistory.Count)
            {
                return;
            }

            _historyIndex++;
            if (_historyIndex == _commandHistory.Count)
            {
                prompt.Input = _historyDraft;
            }
            else
            {
                prompt.Input = _commandHistory[_historyIndex];
            }

            prompt.SetCaret(prompt.Input.Length);
        }
    }

    private void ClearTranscript()
    {
        _document.Transcript.Clear();
        _document.Transcript.Add(new PromptBlock(new BlockId(AllocateNewBlockId()), _defaultPromptText));

        _document.Selection.ActiveRange = null;
        _interaction.Initialize(_document.Transcript);
        SetCaretToEndOfLastPrompt(_document.Transcript);

        _jobProjections.Clear();
        _jobPromptRefs.Clear();
        _activeJobPromptBlocks.Clear();
        _ownedPromptRefs.Clear();
        _commandIndicators.Clear();
        _commandIndicatorStartTicks.Clear();
        _visibleCommandIndicators.Clear();
        _chunkAccumulators.Clear();

        _document.Scroll.ScrollOffsetRows = 0;
        _document.Scroll.IsFollowingTail = true;
        _document.Scroll.ScrollRowsFromBottom = 0;
        _document.Scroll.TopVisualLineAnchor = null;
        _document.Scroll.ScrollbarUi.Visibility = 0;
        _document.Scroll.ScrollbarUi.IsHovering = false;
        _document.Scroll.ScrollbarUi.IsDragging = false;
        _document.Scroll.ScrollbarUi.MsSinceInteraction = 0;
        _document.Scroll.ScrollbarUi.DragGrabOffsetYPx = 0;

        _pendingShellPrompt = false;
        _pendingContentRebuild = true;

        _historyIndex = _commandHistory.Count;
        _historyDraft = string.Empty;
    }

    private void DrainJobEvents()
    {
        var events = _jobScheduler.DrainEvents();
        if (events.Count == 0)
        {
            return;
        }

        var changed = false;
        foreach (var ev in events)
        {
            changed |= ApplyJobEvent(ev);
        }

        if (changed)
        {
            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
        }
    }

    private bool ApplyJobEvent(JobScheduler.PublishedEvent published)
    {
        var jobId = published.JobId;
        EnsureJobProjection(jobId);

        var e = published.Event;
        switch (e)
        {
            case TextEvent text:
                AppendJobText(jobId, text.Stream, text.Text);
                return true;
            case ProgressEvent progress:
                var pct = progress.Fraction is { } f ? $"{Math.Round(f * 100.0)}%" : string.Empty;
                var phase = string.IsNullOrWhiteSpace(progress.Phase) ? "Progress" : progress.Phase;
                AppendJobText(jobId, TextStream.System, $"{phase} {pct}".Trim());
                return true;
            case PromptEvent prompt:
                AppendJobPrompt(jobId, prompt.Prompt);
                return true;
            case ResultEvent result:
                if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.Summary))
                {
                    var summary = string.IsNullOrWhiteSpace(result.Summary) ? string.Empty : $": {result.Summary}";
                    AppendJobText(jobId, TextStream.System, $"(exit {result.ExitCode}){summary}");
                }

                FinalizeActiveJobPromptIfAny(jobId);
                ClearChunkAccumulatorsForJob(jobId);
                _pendingShellPrompt = true;
                EnsureShellPromptAtEndIfNeeded();
                return true;
            default:
                return false;
        }
    }

    private void EnsureJobProjection(JobId jobId)
    {
        if (_jobProjections.ContainsKey(jobId))
        {
            return;
        }

        var headerId = new BlockId(AllocateNewBlockId());
        var insertIndex = Math.Max(0, _document.Transcript.Blocks.Count - 1);
        _document.Transcript.Insert(insertIndex, new TextBlock(headerId, $"$ [job {jobId}]"));
        _jobProjections[jobId] = new JobProjection(headerId, headerId);
    }

    private void AppendJobText(JobId jobId, TextStream stream, string text)
    {
        if (!_jobProjections.TryGetValue(jobId, out var proj))
        {
            EnsureJobProjection(jobId);
            proj = _jobProjections[jobId];
        }

        var mappedStream = MapTextStream(stream);

        if (stream == TextStream.System)
        {
            foreach (var line in SplitLines(text ?? string.Empty))
            {
                var id = new BlockId(AllocateNewBlockId());
                InsertBlockAfter(proj.LastId, new TextBlock(id, line, mappedStream));
                proj = proj with { LastId = id };
            }

            _jobProjections[jobId] = proj;
            return;
        }

        AppendChunkedJobText(jobId, stream, text ?? string.Empty, mappedStream, proj);
    }

    private void InsertBlockAfter(BlockId afterId, IBlock block)
    {
        var blocks = _document.Transcript.Blocks;
        var insertAt = Math.Max(0, blocks.Count - 1);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == afterId)
            {
                insertAt = i + 1;
                break;
            }
        }

        insertAt = Math.Min(insertAt, Math.Max(0, blocks.Count - 1));
        _document.Transcript.Insert(insertAt, block);
    }

    private void AppendChunkedJobText(
        JobId jobId,
        TextStream stream,
        string text,
        ConsoleTextStream mappedStream,
        JobProjection proj)
    {
        var key = (jobId, stream);
        if (!_chunkAccumulators.TryGetValue(key, out var acc))
        {
            acc = new ChunkAccumulator();
            _chunkAccumulators[key] = acc;
        }

        if (acc.PendingCR && text.Length > 0 && text[0] == '\n')
        {
            text = text.Substring(1);
            acc.PendingCR = false;
        }

        var combined = acc.Pending + text;
        var (lines, remainder, endsWithCR) = SplitLinesAndRemainder(combined);
        acc.PendingCR = endsWithCR;

        if (lines.Count == 0 && string.IsNullOrEmpty(remainder))
        {
            acc.Pending = string.Empty;
            _chunkAccumulators[key] = acc;
            _jobProjections[jobId] = proj;
            return;
        }

        if (lines.Count == 0)
        {
            if (acc.ActiveBlockId is { } activeId)
            {
                ReplaceTextBlock(activeId, remainder, mappedStream);
                proj = proj with { LastId = activeId };
            }
            else
            {
                var id = new BlockId(AllocateNewBlockId());
                InsertBlockAfter(proj.LastId, new TextBlock(id, remainder, mappedStream));
                proj = proj with { LastId = id };
                acc.ActiveBlockId = id;
            }

            acc.Pending = remainder;
            _chunkAccumulators[key] = acc;
            _jobProjections[jobId] = proj;
            return;
        }

        if (acc.ActiveBlockId is { } existingActiveId)
        {
            ReplaceTextBlock(existingActiveId, lines[0], mappedStream);
            proj = proj with { LastId = existingActiveId };
            acc.ActiveBlockId = null;
        }
        else
        {
            var firstId = new BlockId(AllocateNewBlockId());
            InsertBlockAfter(proj.LastId, new TextBlock(firstId, lines[0], mappedStream));
            proj = proj with { LastId = firstId };
        }

        for (var i = 1; i < lines.Count; i++)
        {
            var id = new BlockId(AllocateNewBlockId());
            InsertBlockAfter(proj.LastId, new TextBlock(id, lines[i], mappedStream));
            proj = proj with { LastId = id };
        }

        if (!string.IsNullOrEmpty(remainder))
        {
            var id = new BlockId(AllocateNewBlockId());
            InsertBlockAfter(proj.LastId, new TextBlock(id, remainder, mappedStream));
            proj = proj with { LastId = id };
            acc.ActiveBlockId = id;
        }

        acc.Pending = remainder;
        _chunkAccumulators[key] = acc;
        _jobProjections[jobId] = proj;
    }

    private void AppendJobPrompt(JobId jobId, string promptText)
    {
        var blocks = _document.Transcript.Blocks;
        if (blocks.Count == 0)
        {
            return;
        }

        var lastIndex = blocks.Count - 1;
        if (blocks[lastIndex] is PromptBlock existingShellPrompt && existingShellPrompt.Owner is null)
        {
            if (string.IsNullOrEmpty(existingShellPrompt.Input))
            {
                _document.Transcript.RemoveAt(lastIndex);
            }
            else
            {
                var archivedText = existingShellPrompt.Prompt + existingShellPrompt.Input;
                var archived = new TextBlock(existingShellPrompt.Id, archivedText, ConsoleTextStream.Default);
                _document.Transcript.ReplaceAt(lastIndex, archived);
            }
        }

        var promptId = Interlocked.Increment(ref _nextPromptId);
        var promptBlockId = new BlockId(AllocateNewBlockId());
        var block = new PromptBlock(promptBlockId, promptText, new PromptOwner(jobId.Value, promptId))
        {
            Input = string.Empty,
            CaretIndex = 0
        };

        _document.Transcript.Add(block);
        _jobPromptRefs[promptBlockId] = new JobPromptRef(jobId, promptId);
        _activeJobPromptBlocks[jobId] = promptBlockId;
        _pendingShellPrompt = true;
    }

    private void FinalizeActiveJobPromptIfAny(JobId jobId)
    {
        if (!_activeJobPromptBlocks.TryGetValue(jobId, out var promptBlockId))
        {
            return;
        }

        if (!TryGetPrompt(promptBlockId, out var prompt))
        {
            _activeJobPromptBlocks.Remove(jobId);
            _jobPromptRefs.Remove(promptBlockId);
            return;
        }

        ReplacePromptWithArchivedText(promptBlockId, prompt.Prompt + prompt.Input, ConsoleTextStream.Default);
        _activeJobPromptBlocks.Remove(jobId);
        _jobPromptRefs.Remove(promptBlockId);
    }

    private void EnsureShellPromptAtEndIfNeeded()
    {
        if (_ownedPromptRefs.Count > 0)
        {
            return;
        }

        if (_activeJobPromptBlocks.Count > 0)
        {
            return;
        }

        if (HasBlockingActiveBlock())
        {
            return;
        }

        var blocks = _document.Transcript.Blocks;
        if (blocks.Count > 0 && blocks[^1] is PromptBlock prompt && prompt.Owner is null)
        {
            _pendingShellPrompt = false;
            return;
        }

        if (!_pendingShellPrompt && blocks.Count > 0)
        {
            return;
        }

        _document.Transcript.Add(new PromptBlock(new BlockId(AllocateNewBlockId()), _defaultPromptText));
        _pendingShellPrompt = false;
        _pendingContentRebuild = true;
    }

    private bool HasBlockingActiveBlock()
    {
        foreach (var block in _document.Transcript.Blocks)
        {
            if (block is PromptBlock)
            {
                continue;
            }

            if (block is IRunnableBlock runnable && runnable.State == BlockRunState.Running)
            {
                return true;
            }
        }

        return false;
    }

    private void RemoveShellPromptIfPresent(BlockId promptId)
    {
        var blocks = _document.Transcript.Blocks;
        if (blocks.Count == 0)
        {
            return;
        }

        var lastIndex = blocks.Count - 1;
        if (blocks[lastIndex] is PromptBlock prompt && prompt.Id == promptId && prompt.Owner is null)
        {
            _document.Transcript.RemoveAt(lastIndex);
            _pendingContentRebuild = true;
        }
    }

    private void ClearChunkAccumulatorsForJob(JobId jobId)
    {
        if (_chunkAccumulators.Count == 0)
        {
            return;
        }

        List<(JobId, TextStream)>? toRemove = null;
        foreach (var key in _chunkAccumulators.Keys)
        {
            if (key.JobId == jobId)
            {
                toRemove ??= new List<(JobId, TextStream)>();
                toRemove.Add(key);
            }
        }

        if (toRemove is null)
        {
            return;
        }

        foreach (var key in toRemove)
        {
            _chunkAccumulators.Remove(key);
        }
    }

    private void ReplacePromptWithArchivedText(BlockId promptId, string line, ConsoleTextStream stream)
    {
        var blocks = _document.Transcript.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == promptId)
            {
                _document.Transcript.ReplaceAt(i, new TextBlock(promptId, line, stream));
                return;
            }
        }
    }

    private void ReplaceTextBlock(BlockId id, string newText, ConsoleTextStream stream)
    {
        var blocks = _document.Transcript.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == id && blocks[i] is TextBlock)
            {
                _document.Transcript.ReplaceAt(i, new TextBlock(id, newText, stream));
                return;
            }
        }
    }

    private static ConsoleTextStream MapTextStream(TextStream stream) =>
        stream switch
        {
            TextStream.Stdout => ConsoleTextStream.Stdout,
            TextStream.Stderr => ConsoleTextStream.Stderr,
            TextStream.System => ConsoleTextStream.System,
            _ => ConsoleTextStream.Default
        };

    private static (List<string> Lines, string Remainder, bool EndsWithCR) SplitLinesAndRemainder(string text)
    {
        var lines = new List<string>();
        var start = 0;
        var endsWithCR = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\r' && ch != '\n')
            {
                continue;
            }

            lines.Add(text.Substring(start, i - start));
            start = i + 1;

            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                start++;
                i++;
            }
            else if (ch == '\r' && i == text.Length - 1)
            {
                endsWithCR = true;
            }
        }

        var remainder = start >= text.Length ? string.Empty : text.Substring(start);
        return (lines, remainder, endsWithCR);
    }

    private sealed class ChunkAccumulator
    {
        public BlockId? ActiveBlockId;
        public string Pending = string.Empty;
        public bool PendingCR;
    }

    private readonly record struct JobPromptRef(JobId JobId, long PromptId);
    private readonly record struct OwnedPromptRef(long PromptId);

    private bool HandleScrollbarMouse(HostMouseEvent e)
    {
        var layout = _lastLayout;
        if (layout is null)
        {
            return false;
        }

        var ui = _document.Scroll.ScrollbarUi;
        var sb = layout.Scrollbar;
        if (!sb.IsScrollable && !ui.IsDragging)
        {
            return false;
        }

        if (!ui.IsDragging && _interaction.Snapshot.MouseCaptured is not null)
        {
            return false;
        }

        if (e.Kind == HostMouseEventKind.Move && !ui.IsDragging)
        {
            var inTrack = sb.HitTrackRectPx.Contains(e.X, e.Y);
            var wasHovering = ui.IsHovering;
            ui.IsHovering = inTrack;
            if (ui.IsHovering && !wasHovering)
            {
                _scrollbarInteractedThisTick = true;
            }

            return false;
        }

        return e.Kind switch
        {
            HostMouseEventKind.Wheel => HandleWheel(e.WheelDelta, layout),
            HostMouseEventKind.Down => HandleScrollbarMouseDown(e, layout),
            HostMouseEventKind.Move => HandleScrollbarMouseMove(e, layout),
            HostMouseEventKind.Up => HandleScrollbarMouseUp(e),
            _ => false
        };
    }

    private bool HandleWheel(int wheelDelta, LayoutFrame layout)
    {
        if (wheelDelta == 0)
        {
            return false;
        }

        var maxScrollOffsetRows = layout.Grid.Rows <= 0 ? 0 : Math.Max(0, layout.TotalRows - layout.Grid.Rows);
        if (maxScrollOffsetRows == 0)
        {
            return false;
        }

        var deltaRows = -wheelDelta * 3;
        _document.Scroll.ApplyUserScrollDelta(deltaRows, maxScrollOffsetRows);
        _pendingContentRebuild = true;
        _scrollbarInteractedThisTick = true;
        return true;
    }

    private bool HandleScrollbarMouseDown(HostMouseEvent e, LayoutFrame layout)
    {
        if ((e.Buttons & HostMouseButtons.Left) == 0)
        {
            return false;
        }

        var ui = _document.Scroll.ScrollbarUi;
        var sb = layout.Scrollbar;
        if (!sb.IsScrollable)
        {
            return false;
        }

        if (sb.HitThumbRectPx.Contains(e.X, e.Y))
        {
            ui.IsDragging = true;
            ui.IsHovering = true;
            ui.DragGrabOffsetYPx = e.Y - sb.ThumbRectPx.Y;
            _scrollbarInteractedThisTick = true;
            return true;
        }

        if (!sb.HitTrackRectPx.Contains(e.X, e.Y))
        {
            return false;
        }

        var pageRows = Math.Max(1, layout.Grid.Rows);
        var deltaRows = e.Y < sb.ThumbRectPx.Y
            ? -pageRows
            : e.Y >= sb.ThumbRectPx.Y + sb.ThumbRectPx.Height
                ? pageRows
                : 0;

        var maxScrollOffsetRows = layout.Grid.Rows <= 0 ? 0 : Math.Max(0, layout.TotalRows - layout.Grid.Rows);
        _document.Scroll.ApplyUserScrollDelta(deltaRows, maxScrollOffsetRows);
        _pendingContentRebuild = true;
        _scrollbarInteractedThisTick = true;
        return true;
    }

    private bool HandleScrollbarMouseMove(HostMouseEvent e, LayoutFrame layout)
    {
        var ui = _document.Scroll.ScrollbarUi;
        if (!ui.IsDragging)
        {
            return false;
        }

        var sb = layout.Scrollbar;
        var track = sb.TrackRectPx;
        var thumb = sb.ThumbRectPx;

        var grab = Math.Clamp(ui.DragGrabOffsetYPx, 0, thumb.Height);
        var maxThumbTop = track.Y + Math.Max(0, track.Height - thumb.Height);
        var desiredThumbTop = Math.Clamp(e.Y - grab, track.Y, maxThumbTop);

        var cellH = layout.Grid.CellHeightPx;
        var contentHeightPx = layout.TotalRows * cellH;
        var viewportHeightPx = layout.Grid.Rows * cellH;
        var maxScrollYPx = Math.Max(0, contentHeightPx - viewportHeightPx);
        var thumbTravel = Math.Max(0, track.Height - thumb.Height);

        var newScrollYPx = (thumbTravel <= 0 || maxScrollYPx <= 0)
            ? 0
            : (int)((long)(desiredThumbTop - track.Y) * maxScrollYPx / thumbTravel);

        var maxScrollOffsetRows = layout.Grid.Rows <= 0 ? 0 : Math.Max(0, layout.TotalRows - layout.Grid.Rows);
        var newScrollRows = cellH <= 0 ? 0 : (newScrollYPx + (cellH / 2)) / cellH;
        SetScrollOffsetRows(newScrollRows, maxScrollOffsetRows);
        _pendingContentRebuild = true;
        _scrollbarInteractedThisTick = true;
        return true;
    }

    private bool HandleScrollbarMouseUp(HostMouseEvent e)
    {
        var ui = _document.Scroll.ScrollbarUi;
        if (!ui.IsDragging)
        {
            return false;
        }

        if ((e.Buttons & HostMouseButtons.Left) == 0)
        {
            return false;
        }

        ui.IsDragging = false;
        _scrollbarInteractedThisTick = true;
        return true;
    }

    private void SetScrollOffsetRows(int scrollOffsetRows, int maxScrollOffsetRows)
    {
        var clamped = Math.Clamp(scrollOffsetRows, 0, maxScrollOffsetRows);
        _document.Scroll.ScrollOffsetRows = clamped;
        _document.Scroll.IsFollowingTail = clamped >= maxScrollOffsetRows;
        _document.Scroll.ScrollRowsFromBottom = maxScrollOffsetRows - clamped;
    }

    private int AllocateNewBlockId()
    {
        var max = 0;
        foreach (var block in _document.Transcript.Blocks)
        {
            max = Math.Max(max, block.Id.Value);
        }

        return max + 1;
    }

    private static PromptBlock? FindLastPrompt(Transcript transcript)
    {
        for (var i = transcript.Blocks.Count - 1; i >= 0; i--)
        {
            if (transcript.Blocks[i] is PromptBlock prompt)
            {
                return prompt;
            }
        }

        return null;
    }

    private bool TryGetPrompt(BlockId id, out PromptBlock prompt)
    {
        foreach (var block in _document.Transcript.Blocks)
        {
            if (block.Id == id && block is PromptBlock p)
            {
                prompt = p;
                return true;
            }
        }

        prompt = null!;
        return false;
    }

    private bool TryGetBlock(BlockId id, out IBlock block)
    {
        foreach (var candidate in _document.Transcript.Blocks)
        {
            if (candidate.Id == id)
            {
                block = candidate;
                return true;
            }
        }

        block = null!;
        return false;
    }

    private bool TickRunnableBlocks(TimeSpan dt)
    {
        if (dt <= TimeSpan.Zero)
        {
            return false;
        }

        var changed = false;
        foreach (var block in _document.Transcript.Blocks)
        {
            if (block is IRunnableBlock runnable && runnable.State == BlockRunState.Running)
            {
                var stateBefore = runnable.State;
                var textBefore = block is ActivityBlock activity ? activity.ExportText(0, activity.TextLength) : null;
                runnable.Tick(dt);
                var stateAfter = runnable.State;
                var textAfter = block is ActivityBlock activityAfter ? activityAfter.ExportText(0, activityAfter.TextLength) : null;

                if (stateBefore != stateAfter || !string.Equals(textBefore, textAfter, StringComparison.Ordinal))
                {
                    changed = true;
                }
            }
        }

        return changed;
    }



    private void LogOnce(
        GlyphAtlasData atlas,
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
        var viewport = new ConsoleViewport(framebufferWidth, framebufferHeight);
        var grid = FixedCellGrid.FromViewport(viewport, settings);
        return new GridSize(grid.Cols, grid.Rows);
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

        var newGrid = ComputeGrid(width, height, _layoutSettings);

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
            bool restoreAnchor,
            byte caretAlpha,
            double timeSeconds)
    {
        var viewport = new ConsoleViewport(framebufferWidth, framebufferHeight);
        var layout = _layoutEngine.Layout(_document, _layoutSettings, viewport);
        if (restoreAnchor)
        {
            ScrollAnchoring.RestoreFromAnchor(_document.Scroll, layout);
        }

        var maxScrollOffsetRows = layout.Grid.Rows <= 0
            ? 0
            : Math.Max(0, layout.TotalRows - layout.Grid.Rows);

        if (_document.Scroll.IsFollowingTail)
        {
            if (_document.Scroll.ScrollOffsetRows != maxScrollOffsetRows)
            {
                _document.Scroll.ScrollOffsetRows = maxScrollOffsetRows;
                _document.Scroll.ScrollRowsFromBottom = 0;
            }
        }
        else
        {
            _document.Scroll.ScrollOffsetRows = Math.Clamp(_document.Scroll.ScrollOffsetRows, 0, maxScrollOffsetRows);
            _document.Scroll.ScrollRowsFromBottom = maxScrollOffsetRows - _document.Scroll.ScrollOffsetRows;
        }

        var scrollbar = ScrollbarLayouter.Layout(
            layout.Grid,
            layout.TotalRows,
            _document.Scroll.ScrollOffsetRows,
            _document.Settings.Scrollbar);

        if (scrollbar != layout.Scrollbar)
        {
            layout = new LayoutFrame(layout.Grid, layout.Lines, layout.HitTestMap, layout.TotalRows, scrollbar, layout.Scene3DViewports);
        }

        var renderFrame = _renderer.Render(_document, layout, _font, _selectionStyle, timeSeconds: timeSeconds, commandIndicators: _visibleCommandIndicators, caretAlpha: caretAlpha);
        var backendFrame = RenderFrameAdapter.Adapt(renderFrame);
        var builtGrid = backendFrame.BuiltGrid;
        return (backendFrame, builtGrid, layout, renderFrame);
    }

    private static int GetScrollOffsetRows(ConsoleDocument document, LayoutFrame layout)
    {
        var maxScrollOffsetRows = layout.Grid.Rows <= 0
            ? 0
            : Math.Max(0, layout.TotalRows - layout.Grid.Rows);

        return Math.Clamp(document.Scroll.ScrollOffsetRows, 0, maxScrollOffsetRows);
    }

    private abstract record PendingEvent
    {
        public sealed record Text(char Ch) : PendingEvent;

        public sealed record Key(HostKey KeyCode, HostKeyModifiers Mods, bool IsDown) : PendingEvent;

        public sealed record Mouse(HostMouseEvent Event) : PendingEvent;

        public sealed record FileDrop(string Path) : PendingEvent;
    }

    private enum Scene3DDragMode
    {
        Orbit,
        Pan
    }

    private readonly record struct Scene3DCapture(
        BlockId BlockId,
        HostMouseButtons Button,
        Scene3DDragMode Mode,
        int LastX,
        int LastY);

    private sealed record JobProjection(BlockId HeaderId, BlockId LastId);

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}

public readonly record struct FrameTickResult(
    int FramebufferWidth,
    int FramebufferHeight,
    RenderFrame Frame,
    bool? SetVSync,
    bool RequestExit);
