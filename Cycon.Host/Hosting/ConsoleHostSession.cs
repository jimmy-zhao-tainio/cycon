using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using Cycon.Host.Inspect;
using Cycon.Host.Interaction;
using Cycon.Host.Input;
using Cycon.Host.Scrolling;
using Cycon.Host.Rendering;
using Cycon.Host.Services;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;
using Cycon.Rendering.Renderer;
using Cycon.Rendering.Styling;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Hosting;

public sealed class ConsoleHostSession : IBlockCommandSession
{
    private static readonly bool ResizeTrace =
        string.Equals(Environment.GetEnvironmentVariable("CYCON_RESIZE_TRACE"), "1", StringComparison.Ordinal);

    private readonly int _resizeSettleMs;
    private readonly int _rebuildThrottleMs;

    private readonly ConsoleDocument _document;
    private readonly LayoutSettings _layoutSettings;
    private readonly LayoutEngine _layoutEngine;
    private readonly ConsoleRenderer _renderer;
    private readonly RenderPipeline _renderPipeline;
    private readonly IConsoleFont _font;
    private readonly GlyphAtlasData _atlasData;
    private readonly SelectionStyle _selectionStyle;
    private readonly InteractionReducer _interaction = new();
    private readonly IClipboard _clipboard;
    private readonly PendingEventQueue _pendingEventQueue = new();
    private readonly JobScheduler _jobScheduler;
    private readonly CommandDispatcher _commandDispatcher;
    private readonly BlockCommandRegistry _blockCommands;
    private readonly InputPreprocessorRegistry _inputPreprocessors = new();
    private readonly CommandSubmissionService _commandSubmission;
    private readonly JobProjectionService _jobProjectionService;
    private readonly JobEventApplier _jobEventApplier;
    private readonly string _defaultPromptText;
    private readonly PromptLifecycle _promptLifecycle;
    private readonly Dictionary<BlockId, BlockId> _commandIndicators = new();
    private readonly Dictionary<BlockId, long> _commandIndicatorStartTicks = new();
    private readonly Dictionary<BlockId, BlockId> _visibleCommandIndicators = new();
    private readonly List<int> _pendingMesh3DReleases = new();
    private readonly HashSet<int> _pendingMesh3DReleaseSet = new();
    private readonly InputHistory _history;
    private readonly InputCompletionController _completion;
    private bool _pendingExit;

    private readonly ScrollbarController _scrollbarController;
    private readonly InspectModeController _inspectController;

    private LayoutFrame? _lastLayout;
    private RenderFrame? _lastFrame;
    private GridSize _renderedGrid;
    private int _lastBuiltFramebufferWidth;
    private int _lastBuiltFramebufferHeight;
    private bool _pendingResizeRebuild;
    private bool _resizeVsyncDisabled;
    private long _lastRebuildTicks;
    private int _latestFramebufferWidth;
    private int _latestFramebufferHeight;
    private long _lastFramebufferChangeTicks;
    private GridSize _latestGrid;
    private long _lastGridChangeTicks;
    private bool _initialized;
    private bool? _pendingSetVSync;
    private bool _pendingContentRebuild;
    private byte _lastCaretAlpha = 0xFF;
    private long _lastCaretRenderTicks;
    private int _lastSpinnerFrameIndex = -1;
    private long _lastTickTicks;
    private int _tickIndex;
    private int _lastTraceCurFbW = -1;
    private int _lastTraceCurFbH = -1;
    private int _lastTraceBuiltFbW = -1;
    private int _lastTraceBuiltFbH = -1;
    private bool _lastTraceShouldRebuild;

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
        _history = InputHistory.LoadDefault();
        _document = CreateDocument(text);
        _interaction.Initialize(_document.Transcript);
        SetCaretToEndOfLastPrompt(_document.Transcript);
        _defaultPromptText = FindLastPrompt(_document.Transcript)?.Prompt ?? "> ";
        _promptLifecycle = new PromptLifecycle(_document, _defaultPromptText);
        _layoutSettings = new LayoutSettings();
        var fontService = new FontService();
        _font = fontService.CreateDefaultFont(_layoutSettings);
        _layoutSettings.PaddingPolicy = PaddingPolicy.None;
        _layoutSettings.BorderLeftRightPx = 5;
        _layoutSettings.BorderTopBottomPx = 3;
        _layoutSettings.RightGutterPx = _document.Settings.Scrollbar.ThicknessPx;

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
        _blockCommands.RegisterCore(new HelpBlockCommandHandler(_blockCommands));
        _blockCommands.RegisterCore(new EchoBlockCommandHandler());
        _blockCommands.RegisterCore(new AskBlockCommandHandler());
        _blockCommands.RegisterCore(new ClearBlockCommandHandler());
        _blockCommands.RegisterCore(new ExitBlockCommandHandler());
        _blockCommands.RegisterCore(new WaitBlockCommandHandler());
        _blockCommands.RegisterCore(new ProgressBlockCommandHandler());
        configureBlockCommands?.Invoke(_blockCommands);

        _completion = new InputCompletionController(new CommandCompletionProvider(_blockCommands));
        _commandSubmission = new CommandSubmissionService(_blockCommands, _inputPreprocessors);
        _jobProjectionService = new JobProjectionService(
            _document,
            AllocateNewBlockId,
            InsertBlockAfter,
            ReplaceTextBlock);
        _jobEventApplier = new JobEventApplier(
            _jobProjectionService,
            _promptLifecycle,
            AllocateNewBlockId,
            TryGetPromptNullable,
            ReplacePromptWithArchivedText,
            EnsureShellPromptAtEndIfNeeded);
        _scrollbarController = new ScrollbarController(_document, _layoutSettings);
        _inspectController = new InspectModeController(new InspectHostAdapter(this));
        _renderPipeline = new RenderPipeline(_layoutEngine, _renderer, _font, _selectionStyle);
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
        _pendingEventQueue.Enqueue(new PendingEvent.Text(e.Ch));
    }

    public void OnKeyEvent(HostKeyEvent e)
    {
        _pendingEventQueue.Enqueue(new PendingEvent.Key(e.Key, e.Mods, e.IsDown));
    }

    public void OnMouseEvent(HostMouseEvent e)
    {
        _pendingEventQueue.Enqueue(new PendingEvent.Mouse(e));
    }

    public void OnWindowFocusChanged(bool isFocused)
    {
        _inspectController.OnWindowFocusChanged(isFocused);
    }

    public void OnPointerInWindowChanged(bool isInWindow)
    {
        _scrollbarController.OnPointerInWindowChanged(isInWindow);
    }

    public void OnFileDrop(HostFileDropEvent e)
    {
        _pendingEventQueue.Enqueue(new PendingEvent.FileDrop(e.Path));
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

            ReplaceBlockAt(i, new TextBlock(id, $"Render failed: {reason}", ConsoleTextStream.System));
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
        _tickIndex++;
        var dtMs = _lastTickTicks == 0
            ? 0
            : (int)Math.Clamp((nowTicks - _lastTickTicks) * 1000.0 / Stopwatch.Frequency, 0, 250);
        _lastTickTicks = nowTicks;

        var framebufferWidth = _latestFramebufferWidth;
        var framebufferHeight = _latestFramebufferHeight;
        var pendingEvents = DequeuePendingEvents();

        var elapsedSinceFramebufferChangeMs = (nowTicks - _lastFramebufferChangeTicks) * 1000.0 / Stopwatch.Frequency;
        var framebufferSettled = elapsedSinceFramebufferChangeMs >= _resizeSettleMs;
        if (framebufferSettled)
        {
            _document.Scroll.TopVisualLineAnchor = null;

            if (_resizeVsyncDisabled)
            {
                _pendingSetVSync = true;
                _resizeVsyncDisabled = false;
            }
        }

        if (_inspectController.IsActive)
        {
            _inspectController.DrainPendingEvents(pendingEvents, framebufferWidth, framebufferHeight);
            pendingEvents = null;
            if (_inspectController.IsActive)
            {
                _ = _inspectController.TickScene3DKeys(TimeSpan.FromMilliseconds(dtMs));
                var timeSecondsInspect = nowTicks / (double)Stopwatch.Frequency;
                var inspectFrame = _inspectController.BuildInspectFrame(framebufferWidth, framebufferHeight, timeSecondsInspect);
                var backendInspectFrame = RenderFrameAdapter.Adapt(inspectFrame);
                var setVSyncInspect = _pendingSetVSync;
                _pendingSetVSync = null;
                var requestExitInspect = _pendingExit;
                _pendingExit = false;
                return new FrameTickResult(framebufferWidth, framebufferHeight, backendInspectFrame, OverlayFrame: null, setVSyncInspect, requestExitInspect);
            }
        }

        _scrollbarController.BeginTick();

        if (TickRunnableBlocks(TimeSpan.FromMilliseconds(dtMs)))
        {
            _pendingContentRebuild = true;
        }

        UpdateVisibleCommandIndicators(nowTicks);
        EnsureShellPromptAtEndIfNeeded();

        var currentGrid = _latestGrid;

        var elapsedSinceGridChangeMs = (nowTicks - _lastGridChangeTicks) * 1000.0 / Stopwatch.Frequency;
        var gridSettled = elapsedSinceGridChangeMs >= _resizeSettleMs;

        var caretAlphaNow = ComputeCaretAlpha(nowTicks);
        var timeSeconds = nowTicks / (double)Stopwatch.Frequency;

        var renderedGrid = _lastFrame?.BuiltGrid ?? default;

        var gridMismatch = _lastFrame is null || renderedGrid != currentGrid;
        if (gridMismatch) _pendingResizeRebuild = true;

        var framebufferMismatch = _lastFrame is null
            || _lastBuiltFramebufferWidth != framebufferWidth
            || _lastBuiltFramebufferHeight != framebufferHeight;

        var elapsedSinceRebuildMs = (nowTicks - _lastRebuildTicks) * 1000.0 / Stopwatch.Frequency;
        var shouldRebuild = _lastFrame is null
            || _pendingContentRebuild
            || (_pendingResizeRebuild && gridMismatch)
            || (gridMismatch && (gridSettled || elapsedSinceRebuildMs >= _rebuildThrottleMs))
            || (framebufferMismatch && (framebufferSettled || elapsedSinceRebuildMs >= _rebuildThrottleMs));

        if (ResizeTrace)
        {
            var traceCurChanged = framebufferWidth != _lastTraceCurFbW || framebufferHeight != _lastTraceCurFbH;
            var traceBuiltChanged = _lastBuiltFramebufferWidth != _lastTraceBuiltFbW || _lastBuiltFramebufferHeight != _lastTraceBuiltFbH;
            var traceRebuildFlip = shouldRebuild != _lastTraceShouldRebuild;
            var traceMismatch = framebufferMismatch || gridMismatch;
            if (traceCurChanged || traceBuiltChanged || traceRebuildFlip || traceMismatch)
            {
                _lastTraceCurFbW = framebufferWidth;
                _lastTraceCurFbH = framebufferHeight;
                _lastTraceBuiltFbW = _lastBuiltFramebufferWidth;
                _lastTraceBuiltFbH = _lastBuiltFramebufferHeight;
                _lastTraceShouldRebuild = shouldRebuild;
                var builtGrid = _lastFrame?.BuiltGrid ?? default;
                Console.WriteLine(
                    $"[HOST] f={_tickIndex} builtFb={_lastBuiltFramebufferWidth}x{_lastBuiltFramebufferHeight} curFb={framebufferWidth}x{framebufferHeight} builtGrid={builtGrid.Cols}x{builtGrid.Rows} curGrid={currentGrid.Cols}x{currentGrid.Rows} shouldRebuild={shouldRebuild}");
            }
        }

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

                var viewport = new ConsoleViewport(snapW, snapH);
                var result = _renderPipeline.BuildFrame(
                    _document,
                    _layoutSettings,
                    viewport,
                    restoreAnchor,
                    caretAlphaNow,
                    timeSeconds,
                    _visibleCommandIndicators,
                    TakePendingMeshReleases());

                _lastFrame = result.BackendFrame;
                _renderedGrid = result.BuiltGrid;
                _lastLayout = result.Layout;
                _lastBuiltFramebufferWidth = snapW;
                _lastBuiltFramebufferHeight = snapH;
                _lastRebuildTicks = passNowTicks;
                _lastCaretAlpha = caretAlphaNow;
                _lastSpinnerFrameIndex = ComputeSpinnerFrameIndex(passNowTicks);
                _lastCaretRenderTicks = passNowTicks;
                ClearPendingMeshReleases();

                var verifyGrid = _latestGrid;
                var verifyW = _latestFramebufferWidth;
                var verifyH = _latestFramebufferHeight;
                if (_renderedGrid == verifyGrid && _lastBuiltFramebufferWidth == verifyW && _lastBuiltFramebufferHeight == verifyH)
                {
                    break;
                }

                snapW = verifyW;
                snapH = verifyH;
                snapGrid = verifyGrid;
            }

            _pendingResizeRebuild = false;
            _pendingContentRebuild = false;

            framebufferWidth = snapW;
            framebufferHeight = snapH;
            currentGrid = snapGrid;
        }

        DrainPendingEvents(pendingEvents, framebufferWidth, framebufferHeight);

        if (_inspectController.IsActive)
        {
            var timeSecondsInspect = nowTicks / (double)Stopwatch.Frequency;
            var inspectFrame = _inspectController.BuildInspectFrame(framebufferWidth, framebufferHeight, timeSecondsInspect);
            var backendInspectFrame = RenderFrameAdapter.Adapt(inspectFrame);
            var setVSyncInspect = _pendingSetVSync;
            _pendingSetVSync = null;
            var requestExitInspect = _pendingExit;
            _pendingExit = false;
            return new FrameTickResult(framebufferWidth, framebufferHeight, backendInspectFrame, OverlayFrame: null, setVSyncInspect, requestExitInspect);
        }

        DrainJobEvents();

        _scrollbarController.AdvanceAnimation(dtMs);

        if (_pendingContentRebuild)
        {
            var viewport = new ConsoleViewport(framebufferWidth, framebufferHeight);
            var result = _renderPipeline.BuildFrame(
                _document,
                _layoutSettings,
                viewport,
                restoreAnchor: false,
                caretAlpha: caretAlphaNow,
                timeSeconds: timeSeconds,
                _visibleCommandIndicators,
                TakePendingMeshReleases());

            _lastFrame = result.BackendFrame;
            _renderedGrid = result.BuiltGrid;
            _lastLayout = result.Layout;
            _lastRebuildTicks = Stopwatch.GetTimestamp();
            _lastCaretAlpha = caretAlphaNow;
            _lastSpinnerFrameIndex = ComputeSpinnerFrameIndex(_lastRebuildTicks);
            _lastCaretRenderTicks = _lastRebuildTicks;
            _pendingContentRebuild = false;
            ClearPendingMeshReleases();
        }

        MaybeUpdateOverlays(framebufferWidth, framebufferHeight, nowTicks, framebufferSettled);

        if (_lastFrame is null)
        {
            throw new InvalidOperationException("Tick invariant violated: frame must be available.");
        }

        _scrollbarController.UpdateTotalRows(_lastLayout);
        var overlayFrame = _scrollbarController.BuildOverlayFrame(
            new PxRect(0, 0, framebufferWidth, framebufferHeight),
            _document.Settings.DefaultTextStyle.ForegroundRgba);

        var setVSync = _pendingSetVSync;
        _pendingSetVSync = null;
        var requestExit = _pendingExit;
        _pendingExit = false;
        return new FrameTickResult(framebufferWidth, framebufferHeight, _lastFrame, overlayFrame, setVSync, requestExit);
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
        if (caretAlpha == _lastCaretAlpha &&
            spinnerIndex == _lastSpinnerFrameIndex)
        {
            return;
        }

        var timeSeconds = nowTicks / (double)Stopwatch.Frequency;
        var renderFrame = _renderer.Render(_document, _lastLayout, _font, _selectionStyle, timeSeconds: timeSeconds, commandIndicators: _visibleCommandIndicators, caretAlpha: caretAlpha, meshReleases: TakePendingMeshReleases());
        var backendFrame = RenderFrameAdapter.Adapt(renderFrame);
        ClearPendingMeshReleases();
        _lastFrame = backendFrame;
        _renderedGrid = backendFrame.BuiltGrid;
        _lastCaretAlpha = caretAlpha;
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

    private List<PendingEvent>? DequeuePendingEvents()
    {
        return _pendingEventQueue.DequeueAll();
    }

    private void DrainPendingEvents(List<PendingEvent>? events, int framebufferWidth, int framebufferHeight)
    {
        if (events is null || events.Count == 0)
        {
            return;
        }

        EnsureLayoutExists(framebufferWidth, framebufferHeight);
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is PendingEvent.FileDrop fileDrop)
            {
                HandleFileDrop(fileDrop.Path);
                EnsureLayoutExists(framebufferWidth, framebufferHeight);
                _scrollbarController.UpdateTotalRows(_lastLayout);
                continue;
            }

            if (events[i] is PendingEvent.Text textInput && !char.IsControl(textInput.Ch))
            {
                // No embedded blocks may steal keyboard focus; text input always goes to the transcript interaction model.
            }

            if (events[i] is PendingEvent.Key key && key.KeyCode != HostKey.Unknown)
            {
                // No embedded blocks may steal keyboard focus; keyboard events are handled by the transcript interaction model.
            }

            if (events[i] is PendingEvent.Mouse mouseRaw && _lastLayout is not null)
            {
                var mouseEvent = mouseRaw.Event;
                var viewportRectPx = new PxRect(0, 0, _latestFramebufferWidth, _latestFramebufferHeight);

                var consumed = _scrollbarController.TryHandleMouse(
                    mouseEvent,
                    viewportRectPx,
                    _interaction.Snapshot,
                    _lastLayout,
                    out var scrollChanged);

                if (consumed)
                {
                    if (scrollChanged ||
                        (mouseEvent.Kind == HostMouseEventKind.Move && _document.Scroll.ScrollbarUi.IsDragging))
                    {
                        _pendingContentRebuild = true;
                    }

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
        var viewport = new ConsoleViewport(framebufferWidth, framebufferHeight);
        var result = _renderPipeline.BuildFrame(
            _document,
            _layoutSettings,
            viewport,
            restoreAnchor: false,
            caretAlpha: caretAlphaNow,
            timeSeconds: timeSeconds,
            _visibleCommandIndicators,
            TakePendingMeshReleases());

        _lastFrame = result.BackendFrame;
        _renderedGrid = result.BuiltGrid;
        _lastLayout = result.Layout;
        _lastBuiltFramebufferWidth = framebufferWidth;
        _lastBuiltFramebufferHeight = framebufferHeight;
        _lastRebuildTicks = nowTicks;
        _lastCaretAlpha = caretAlphaNow;
        _lastSpinnerFrameIndex = ComputeSpinnerFrameIndex(nowTicks);
        _lastCaretRenderTicks = nowTicks;
        ClearPendingMeshReleases();
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
            if (ShouldResetCompletion(action))
            {
                _completion.Reset();
            }

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
                case HostAction.Autocomplete ac:
                    AutocompletePrompt(ac.PromptId, ac.Delta);
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

    private static bool ShouldResetCompletion(HostAction action) =>
        action is HostAction.InsertText or
        HostAction.Backspace or
        HostAction.MoveCaret or
        HostAction.SetCaret or
        HostAction.NavigateHistory or
        HostAction.SubmitPrompt;

    private void AutocompletePrompt(BlockId promptId, int delta)
    {
        if (!TryGetPrompt(promptId, out var prompt))
        {
            return;
        }

        if (prompt.Owner is not null)
        {
            return;
        }

        var reverse = delta < 0;
        if (!_completion.TryHandleTab(prompt.Input, prompt.CaretIndex, reverse, out var newInput, out var newCaret, out var matchesLine))
        {
            return;
        }

        if (matchesLine is not null)
        {
            var id = new BlockId(AllocateNewBlockId());
            InsertBlockBefore(promptId, new TextBlock(id, matchesLine, ConsoleTextStream.System));
            _document.Scroll.IsFollowingTail = true;
        }

        prompt.Input = newInput;
        prompt.SetCaret(Math.Clamp(newCaret, 0, prompt.Input.Length));
        _pendingContentRebuild = true;
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

        if (_promptLifecycle.PendingShellPrompt)
        {
            EnsureShellPromptAtEndIfNeeded();
        }

        if (block is PromptBlock { Owner: not null } && _promptLifecycle.TryGetOwnedPrompt(block.Id, out _))
        {
            if (TryGetPrompt(block.Id, out var ownedPrompt))
            {
                ReplacePromptWithArchivedText(block.Id, ownedPrompt.Prompt + ownedPrompt.Input, ConsoleTextStream.Default);
            }

            _promptLifecycle.RemoveOwnedPrompt(block.Id);
            _promptLifecycle.PendingShellPrompt = true;
            EnsureShellPromptAtEndIfNeeded();
        }

        if (block is PromptBlock { Owner: not null } && _promptLifecycle.TryGetJobPrompt(block.Id, out var jobPrompt))
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

        if (_promptLifecycle.TryGetOwnedPrompt(promptId, out _))
        {
            var promptLine = prompt.Prompt + command;
            ReplacePromptWithArchivedText(promptId, promptLine, ConsoleTextStream.Default);

            _promptLifecycle.RemoveOwnedPrompt(promptId);
            _promptLifecycle.PendingShellPrompt = true;
            EnsureShellPromptAtEndIfNeeded();

            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
            return;
        }

        if (_promptLifecycle.TryGetJobPrompt(promptId, out var jobPrompt))
        {
            var promptLine = prompt.Prompt + command;
            ReplacePromptWithArchivedText(promptId, promptLine, ConsoleTextStream.Default);

            if (_jobScheduler.TryGetJob(jobPrompt.JobId, out var job))
            {
                _ = job.SendInputAsync(command, CancellationToken.None);
            }
            else
            {
                _jobProjectionService.AppendText(jobPrompt.JobId, TextStream.System, "Interactive job is no longer running.");
            }

            _promptLifecycle.RemoveJobPrompt(promptId);
            _promptLifecycle.RemoveActiveJobPrompt(jobPrompt.JobId);
            _promptLifecycle.PendingShellPrompt = true;

            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
        }
        else
        {
            var insertIndex = Math.Max(0, _document.Transcript.Blocks.Count - 1);
            var headerId = new BlockId(AllocateNewBlockId());
            _document.Transcript.Insert(insertIndex, new TextBlock(headerId, prompt.Prompt + command));

            RecordCommandHistory(command);

            var submitResult = _commandSubmission.Submit(command, headerId, promptId, this);
            if (submitResult.IsParseFailed)
            {
                prompt.Input = string.Empty;
                prompt.SetCaret(0);
                _history.ResetNavigation();
                return;
            }

            if (submitResult.Handled)
            {
                _document.Scroll.IsFollowingTail = true;
                _pendingContentRebuild = true;

                if (submitResult.StartedBlockingActivity)
                {
                    RemoveShellPromptIfPresent(promptId);
                    _promptLifecycle.PendingShellPrompt = true;
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
            _history.ResetNavigation();
        }
    }

    private void HandleFileDrop(string path)
    {
        var blocks = _document.Transcript.Blocks;
        if (blocks.Count == 0)
        {
            return;
        }

        var commandText = $"inspect {QuoteForCommandLineParser(path)}";

        var insertIndex = Math.Max(0, blocks.Count - 1);
        var headerId = new BlockId(AllocateNewBlockId());
        _document.Transcript.Insert(insertIndex, new TextBlock(headerId, _defaultPromptText + commandText, ConsoleTextStream.Default));

        RecordCommandHistory(commandText);

        var shellPromptId = FindLastPrompt(_document.Transcript)?.Id ?? headerId;
        var submitResult = _commandSubmission.Submit(commandText, headerId, shellPromptId, this);
        if (submitResult.IsParseFailed)
        {
            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
            return;
        }

        if (submitResult.Handled)
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
        _history.RecordSubmitted(commandText);
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

        if (_history.TryNavigate(prompt.Input, delta, out var updated))
        {
            prompt.Input = updated;
            prompt.SetCaret(prompt.Input.Length);
        }
    }

    private void ClearTranscript()
    {
        _completion.Reset();
        QueueMeshReleasesForAllBlocks();
        _document.Transcript.Clear();
        _document.Transcript.Add(new PromptBlock(new BlockId(AllocateNewBlockId()), _defaultPromptText));

        _document.Selection.ActiveRange = null;
        _interaction.Initialize(_document.Transcript);
        SetCaretToEndOfLastPrompt(_document.Transcript);

        _jobProjectionService.Clear();
        _promptLifecycle.Clear();
        _commandIndicators.Clear();
        _commandIndicatorStartTicks.Clear();
        _visibleCommandIndicators.Clear();

        _document.Scroll.ScrollOffsetRows = 0;
        _document.Scroll.IsFollowingTail = true;
        _document.Scroll.ScrollRowsFromBottom = 0;
        _document.Scroll.TopVisualLineAnchor = null;
        _document.Scroll.ScrollbarUi.Visibility = 0;
        _document.Scroll.ScrollbarUi.IsHovering = false;
        _document.Scroll.ScrollbarUi.IsDragging = false;
        _document.Scroll.ScrollbarUi.MsSinceInteraction = 0;
        _document.Scroll.ScrollbarUi.DragGrabOffsetYPx = 0;

        _pendingContentRebuild = true;

        _history.ResetNavigation();
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
            changed |= _jobEventApplier.Apply(ev);
        }

        if (changed)
        {
            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
        }
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

    private void InsertBlockBefore(BlockId beforeId, IBlock block)
    {
        var blocks = _document.Transcript.Blocks;
        var insertAt = Math.Max(0, blocks.Count - 1);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == beforeId)
            {
                insertAt = i;
                break;
            }
        }

        insertAt = Math.Clamp(insertAt, 0, blocks.Count);
        _document.Transcript.Insert(insertAt, block);
    }

    private void EnsureShellPromptAtEndIfNeeded()
    {
        if (_promptLifecycle.HasOwnedPrompts)
        {
            return;
        }

        if (_promptLifecycle.HasActiveJobPrompts)
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
            _promptLifecycle.PendingShellPrompt = false;
            return;
        }

        if (!_promptLifecycle.PendingShellPrompt && blocks.Count > 0)
        {
            return;
        }

        _promptLifecycle.EnsureShellPromptAtEndIfNeeded(AllocateNewBlockId);
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
        _promptLifecycle.RemoveShellPromptIfPresent(promptId, RemoveBlockAt);
        _pendingContentRebuild = true;
    }

    private void ReplacePromptWithArchivedText(BlockId promptId, string line, ConsoleTextStream stream)
    {
        var blocks = _document.Transcript.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == promptId)
            {
                ReplaceBlockAt(i, new TextBlock(promptId, line, stream));
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
                ReplaceBlockAt(i, new TextBlock(id, newText, stream));
                return;
            }
        }
    }

    private sealed class InspectHostAdapter : IInspectHost
    {
        private readonly ConsoleHostSession _session;

        public InspectHostAdapter(ConsoleHostSession session)
        {
            _session = session;
        }

        public ConsoleDocument Document => _session._document;
        public IConsoleFont Font => _session._font;
        public ConsoleRenderer Renderer => _session._renderer;
        public SelectionStyle SelectionStyle => _session._selectionStyle;
        public IReadOnlyList<IBlock> TranscriptBlocks => _session._document.Transcript.Blocks;

        public void InsertTranscriptBlock(int index, IBlock block)
        {
            var blocks = _session._document.Transcript.Blocks;
            var insertAt = Math.Clamp(index, 0, blocks.Count);
            _session._document.Transcript.Insert(insertAt, block);
        }

        public BlockId AllocateBlockId() => new(_session.AllocateNewBlockId());

        public void HandleFileDrop(string path) => _session.HandleFileDrop(path);

        public void RequestContentRebuild() => _session._pendingContentRebuild = true;

        public IReadOnlyList<int>? TakePendingMeshReleases() => _session.TakePendingMeshReleases();

        public void ClearPendingMeshReleases() => _session.ClearPendingMeshReleases();
    }

    private void QueueMeshReleasesForAllBlocks()
    {
        foreach (var block in _document.Transcript.Blocks)
        {
            if (block is IMesh3DResourceOwner owner)
            {
                QueueMeshRelease(owner.MeshId);
            }
        }
    }

    private void QueueMeshRelease(int meshId)
    {
        if (_pendingMesh3DReleaseSet.Add(meshId))
        {
            _pendingMesh3DReleases.Add(meshId);
        }
    }

    private IReadOnlyList<int>? TakePendingMeshReleases()
    {
        if (_pendingMesh3DReleases.Count == 0)
        {
            return null;
        }

        // Safe to pass the live list to the renderer; it enumerates synchronously.
        return _pendingMesh3DReleases;
    }

    private void ClearPendingMeshReleases()
    {
        _pendingMesh3DReleases.Clear();
        _pendingMesh3DReleaseSet.Clear();
    }

    private void RemoveBlockAt(int index)
    {
        var blocks = _document.Transcript.Blocks;
        if ((uint)index >= (uint)blocks.Count)
        {
            return;
        }

        if (blocks[index] is IMesh3DResourceOwner owner)
        {
            QueueMeshRelease(owner.MeshId);
        }

        _document.Transcript.RemoveAt(index);
    }

    private void ReplaceBlockAt(int index, IBlock replacement)
    {
        var blocks = _document.Transcript.Blocks;
        if ((uint)index >= (uint)blocks.Count)
        {
            return;
        }

        if (blocks[index] is IMesh3DResourceOwner owner)
        {
            QueueMeshRelease(owner.MeshId);
        }

        _document.Transcript.ReplaceAt(index, replacement);
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

    private PromptBlock? TryGetPromptNullable(BlockId id)
    {
        return TryGetPrompt(id, out var prompt) ? prompt : null;
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

    private static int GetScrollOffsetRows(ConsoleDocument document, LayoutFrame layout)
    {
        var maxScrollOffsetRows = layout.Grid.Rows <= 0
            ? 0
            : Math.Max(0, layout.TotalRows - layout.Grid.Rows);

        return Math.Clamp(document.Scroll.ScrollOffsetRows, 0, maxScrollOffsetRows);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    int IBlockCommandSession.AllocateNewBlockId() => AllocateNewBlockId();

    void IBlockCommandSession.InsertBlockAfter(BlockId afterId, IBlock block) => InsertBlockAfter(afterId, block);

    bool IBlockCommandSession.TryGetPrompt(BlockId id, out PromptBlock prompt) => TryGetPrompt(id, out prompt);

    void IBlockCommandSession.AppendOwnedPromptInternal(string promptText)
    {
        _promptLifecycle.AppendOwnedPrompt(
            promptText,
            AllocateNewBlockId,
            RemoveBlockAt,
            ReplaceBlockAt);
    }

    void IBlockCommandSession.AttachIndicator(BlockId commandEchoId, BlockId activityBlockId)
    {
        _commandIndicators[commandEchoId] = activityBlockId;
        _pendingContentRebuild = true;
    }

    void IBlockCommandSession.OpenInspect(InspectKind kind, string path, string title, IBlock viewBlock, string receiptLine, BlockId commandEchoId)
    {
        _inspectController.OpenInspect(kind, path, title, viewBlock, receiptLine, commandEchoId);
    }

    void IBlockCommandSession.ClearTranscript() => ClearTranscript();

    void IBlockCommandSession.RequestExit()
    {
        _pendingExit = true;
        _pendingContentRebuild = true;
    }
}

public readonly record struct FrameTickResult(
    int FramebufferWidth,
    int FramebufferHeight,
    RenderFrame Frame,
    RenderFrame? OverlayFrame,
    bool? SetVSync,
    bool RequestExit);
