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
using Cycon.Host.Commands.Blocks;
using Cycon.Host.Commands.Handlers;
using Cycon.Host.FileSystem;
using Cycon.Host.Inspect;
using Cycon.Host.Interaction;
using Cycon.Host.Input;
using Cycon.Host.Scrolling;
using Cycon.Host.Rendering;
using Cycon.Host.Services;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;
using Cycon.Layout.HitTesting;
using Cycon.Rendering.Renderer;
using Cycon.Rendering.Styling;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Hosting;

public sealed class ConsoleHostSession : IBlockCommandSession
{

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
    private readonly CommandSubmissionService _commandSubmission;
    private readonly JobProjectionService _jobProjectionService;
    private readonly JobEventApplier _jobEventApplier;
    private readonly CommandHost _commandHost;
    private readonly CommandHostViewAdapter _commandHostView;
    private readonly string _defaultPromptText;
    private readonly PromptLifecycle _promptLifecycle;
    private readonly Dictionary<BlockId, BlockId> _commandIndicators = new();
    private readonly Dictionary<BlockId, long> _commandIndicatorStartTicks = new();
    private readonly Dictionary<BlockId, BlockId> _visibleCommandIndicators = new();
    private readonly List<int> _pendingMesh3DReleases = new();
    private readonly HashSet<int> _pendingMesh3DReleaseSet = new();
    private readonly ResizeCoordinator _resizeCoordinator;
    private bool _pendingExit;

    private readonly ScrollbarController _scrollbarController;
    private readonly InspectModeController _inspectController;
    private readonly Scene3DPointerController _inlineScene3DPointer = new();
    private readonly SystemFileSystem _fileSystem = new();
    private readonly Dictionary<char, string> _lastDirectoryPerDrive = new();
    private string _currentDirectory = string.Empty;
    private string _homeDirectory = string.Empty;
    private HostCursorKind _cursorKind;
    private HitTestActionSpan? _hoveredActionSpan;
    private int _hoveredActionSpanIndex = -1;
    private int _selectedActionSpanIndex = -1;
    private BlockId? _selectedActionSpanBlockId;
    private string? _selectedActionSpanCommandText;
    private long _lastActionSpanClickTicks;
    private int _lastActionSpanClickIndex = -1;
    private BlockId? _lastActionSpanClickBlockId;
    private int _lastMouseX;
    private int _lastMouseY;
    private bool _hasMousePosition;
    private BlockId? _capturedInlineViewportBlockId;
    private BlockId? _focusedInlineViewportBlockId;
    private Scene3DNavKeys _focusedScene3DNavKeysDown = Scene3DNavKeys.None;
    private int _suppressNextTextInputTickIndex;

    private LayoutFrame? _lastLayout;
    private RenderFrame? _lastFrame;
    private bool _initialized;
    private bool _pendingContentRebuild;
    private byte _lastCaretAlpha = 0xFF;
    private long _lastCaretRenderTicks;
    private int _lastSpinnerFrameIndex = -1;
    private long _lastTickTicks;
    private int _tickIndex;
    private readonly PromptCaretController _caret = new();
    private bool _windowIsFocused = true;

    private ConsoleHostSession(
        string text,
        IClipboard clipboard,
        int resizeSettleMs,
        int rebuildThrottleMs,
        Action? wake,
        Action<BlockCommandRegistry>? configureBlockCommands)
    {
        _clipboard = clipboard;
        _document = CreateDocument(text);
        _interaction.Initialize(_document.Transcript);
        SetCaretToEndOfLastPrompt(_document.Transcript);
        InitializeHomeDirectory();
        InitializeWorkingDirectory();
        _defaultPromptText = FindLastPrompt(_document.Transcript)?.Prompt ?? "> ";
        _promptLifecycle = new PromptLifecycle(_document, _defaultPromptText);
        _layoutSettings = new LayoutSettings();
        var fontService = new FontService();
        _font = fontService.CreateDefaultFont(_layoutSettings);
        _layoutSettings.PaddingPolicy = PaddingPolicy.None;
        _layoutSettings.BorderLeftRightPx = 5;
        _layoutSettings.BorderTopBottomPx = 3;
        _layoutSettings.RightGutterPx = Math.Max(0, _document.Settings.Scrollbar.ThicknessPx - 6);

        _atlasData = _font.Atlas;
        _renderer = new ConsoleRenderer();
        _selectionStyle = SelectionStyle.Default;
        _layoutEngine = new LayoutEngine();

        _jobScheduler = new JobScheduler(wake);
        var registry = new CommandRegistry();
        registry.Register(new EchoCommandHandler());
        registry.Register(new AskCommandHandler());
        _commandDispatcher = new CommandDispatcher(
            registry,
            _jobScheduler,
            EmptyServiceProvider.Instance,
            cwdProvider: () => _currentDirectory,
            envProvider: static () => new Dictionary<string, string>());

        _commandHost = new CommandHost(configureBlockCommands);
        _commandHostView = new CommandHostViewAdapter(this);
        _commandSubmission = new CommandSubmissionService(_commandHost.BlockCommands, _commandHost.InputPreprocessors);
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
        _resizeCoordinator = new ResizeCoordinator(resizeSettleMs, rebuildThrottleMs, _layoutSettings);

        UpdateShellPromptTextIfPresent();
    }

    public static ConsoleHostSession CreateVga(
        string text,
        IClipboard clipboard,
        int resizeSettleMs = 80,
        int rebuildThrottleMs = 80,
        Action? wake = null,
        Action<BlockCommandRegistry>? configureBlockCommands = null)
    {
        return new ConsoleHostSession(text, clipboard, resizeSettleMs, rebuildThrottleMs, wake, configureBlockCommands);
    }

    public GlyphAtlasData Atlas => _atlasData;

    public HostCursorKind CursorKind => _cursorKind;

    string IBlockCommandSession.CurrentDirectory => _currentDirectory;

    string IBlockCommandSession.HomeDirectory => _homeDirectory;

    string IBlockCommandSession.ResolvePath(string path) => ConsolePathResolver.ResolvePath(_currentDirectory, _lastDirectoryPerDrive, path);

    bool IBlockCommandSession.TrySetCurrentDirectory(string directory, out string error) => TrySetCurrentDirectory(directory, out error);

    Cycon.BlockCommands.IFileSystem IBlockCommandSession.FileSystem => _fileSystem;

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
        _windowIsFocused = isFocused;
        _pendingContentRebuild = true;
        _inspectController.OnWindowFocusChanged(isFocused);
    }

    public void OnPointerInWindowChanged(bool isInWindow)
    {
        _scrollbarController.OnPointerInWindowChanged(isInWindow);
        if (!isInWindow)
        {
            if (_hoveredActionSpan is not null)
            {
                _hoveredActionSpan = null;
                _hoveredActionSpanIndex = -1;
                _pendingContentRebuild = true;
            }

            _cursorKind = HostCursorKind.Default;
        }
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
        var nowTicks = Stopwatch.GetTimestamp();
        _resizeCoordinator.Initialize(initialFbW, initialFbH, nowTicks);
        _initialized = true;
    }

    public void OnFramebufferResized(int fbW, int fbH)
    {
        var nowTicks = Stopwatch.GetTimestamp();
        var result = _resizeCoordinator.OnFramebufferResized(fbW, fbH, nowTicks, _lastLayout);
        if (result.CaptureAnchor && _lastLayout is not null)
        {
            ScrollAnchoring.CaptureAnchor(_document.Scroll, _lastLayout);
        }
    }

    public FrameTickResult Tick(long nowTicks)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Session must be initialized before ticking.");
        }

        _tickIndex++;
        var dtMs = _lastTickTicks == 0
            ? 0
            : (int)Math.Clamp((nowTicks - _lastTickTicks) * 1000.0 / Stopwatch.Frequency, 0, 250);
        _lastTickTicks = nowTicks;

        var resizeSnapshot = _resizeCoordinator.GetLatestSnapshot();
        var framebufferWidth = resizeSnapshot.FramebufferWidth;
        var framebufferHeight = resizeSnapshot.FramebufferHeight;
        var pendingEvents = DequeuePendingEvents();
        var resizePlan = _resizeCoordinator.PlanForTick(
            nowTicks,
            _lastFrame is not null,
            _lastFrame?.BuiltGrid ?? default,
            _pendingContentRebuild);

        if (resizePlan.ClearTopAnchor)
        {
            _document.Scroll.TopVisualLineAnchor = null;
        }

        if (_inspectController.IsActive)
        {
            _caret.SetSuppressed(true, nowTicks);
            _inspectController.DrainPendingEvents(pendingEvents, framebufferWidth, framebufferHeight);
            ApplyInspectActions(_inspectController.DrainActions());
            pendingEvents = null;
            if (_inspectController.IsActive)
            {
                _ = _inspectController.TickScene3DKeys(TimeSpan.FromMilliseconds(dtMs));
                var timeSecondsInspect = nowTicks / (double)Stopwatch.Frequency;
                var inspectFrame = _inspectController.BuildInspectFrame(
                    framebufferWidth,
                    framebufferHeight,
                    timeSecondsInspect,
                    TakePendingMeshReleases());
                var backendInspectFrame = RenderFrameAdapter.Adapt(inspectFrame);
                ClearPendingMeshReleases();
                var setVSyncInspect = _resizeCoordinator.ConsumeVSyncRequest();
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

        var currentGrid = resizePlan.CurrentGrid;

        var typingPending = pendingEvents is not null && IsPromptFocused() && HasTypingTrigger(pendingEvents);

        _caret.SetSuppressed(false, nowTicks);
        _caret.SetPromptFocused(IsPromptFocused(), nowTicks);
        if (typingPending)
        {
            _caret.OnTyped(nowTicks);
        }
        _caret.Update(nowTicks);
        var caretAlphaNow = _caret.SampleAlpha(nowTicks);
        if (!typingPending && _document.Selection.ActiveRange is { } range && range.Anchor != range.Caret)
        {
            caretAlphaNow = 0;
            _caret.SetSuppressed(true, nowTicks);
        }
        var timeSeconds = nowTicks / (double)Stopwatch.Frequency;

        var renderedGrid = _lastFrame?.BuiltGrid ?? default;
        var gridMismatch = resizePlan.GridMismatch;
        var framebufferMismatch = resizePlan.FramebufferMismatch;
        var shouldRebuild = resizePlan.ShouldRebuild;


        if (shouldRebuild)
        {
            var restoreAnchor = resizePlan.PendingResizeRebuild;

            var snapW = framebufferWidth;
            var snapH = framebufferHeight;
            var snapGrid = currentGrid;

            var rebuildPasses = resizePlan.RebuildPasses;
            for (var pass = 0; pass < rebuildPasses; pass++)
            {
                var passNowTicks = Stopwatch.GetTimestamp();

                FreezeTranscriptFollowTailWhileViewportFocused();
                var viewport = new ConsoleViewport(snapW, snapH);
                var result = _renderPipeline.BuildFrame(
                    _document,
                    _layoutSettings,
                    viewport,
                    restoreAnchor,
                    caretAlphaNow,
                    timeSeconds,
                    _visibleCommandIndicators,
                    TakePendingMeshReleases(),
                    focusedViewportBlockId: _focusedInlineViewportBlockId,
                    selectedActionSpanBlockId: _selectedActionSpanBlockId,
                    selectedActionSpanCommandText: _selectedActionSpanCommandText,
                    selectedActionSpanIndex: _selectedActionSpanIndex,
                    hasMousePosition: _hasMousePosition,
                    mouseX: _lastMouseX,
                    mouseY: _lastMouseY);

                _lastFrame = result.BackendFrame;
                _lastLayout = result.Layout;
                _resizeCoordinator.NotifyFrameBuilt(snapW, snapH, result.BuiltGrid, passNowTicks);
                _lastCaretAlpha = caretAlphaNow;
                _lastSpinnerFrameIndex = ComputeSpinnerFrameIndex(passNowTicks);
                _lastCaretRenderTicks = passNowTicks;
                ClearPendingMeshReleases();

                var verifySnapshot = _resizeCoordinator.GetLatestSnapshot();
                var verifyGrid = verifySnapshot.Grid;
                var verifyW = verifySnapshot.FramebufferWidth;
                var verifyH = verifySnapshot.FramebufferHeight;
                if (result.BuiltGrid == verifyGrid &&
                    _resizeCoordinator.GetBuiltSnapshot().FramebufferWidth == verifyW &&
                    _resizeCoordinator.GetBuiltSnapshot().FramebufferHeight == verifyH)
                {
                    break;
                }

                snapW = verifyW;
                snapH = verifyH;
                snapGrid = verifyGrid;
            }

            _resizeCoordinator.ClearPendingResizeRebuild();
            _pendingContentRebuild = false;

            framebufferWidth = snapW;
            framebufferHeight = snapH;
            currentGrid = snapGrid;
        }

        DrainPendingEvents(pendingEvents, framebufferWidth, framebufferHeight, nowTicks);

        if (TickFocusedScene3DKeys(TimeSpan.FromMilliseconds(dtMs)))
        {
            _pendingContentRebuild = true;
        }

        if (_inspectController.IsActive)
        {
            var timeSecondsInspect = nowTicks / (double)Stopwatch.Frequency;
            var inspectFrame = _inspectController.BuildInspectFrame(
                framebufferWidth,
                framebufferHeight,
                timeSecondsInspect,
                TakePendingMeshReleases());
            var backendInspectFrame = RenderFrameAdapter.Adapt(inspectFrame);
            ClearPendingMeshReleases();
            var setVSyncInspect = _resizeCoordinator.ConsumeVSyncRequest();
            var requestExitInspect = _pendingExit;
            _pendingExit = false;
            return new FrameTickResult(framebufferWidth, framebufferHeight, backendInspectFrame, OverlayFrame: null, setVSyncInspect, requestExitInspect);
        }

        DrainJobEvents();

        _scrollbarController.AdvanceAnimation(dtMs);

        if (_pendingContentRebuild)
        {
            FreezeTranscriptFollowTailWhileViewportFocused();
            var viewport = new ConsoleViewport(framebufferWidth, framebufferHeight);
            var result = _renderPipeline.BuildFrame(
                _document,
                _layoutSettings,
                viewport,
                restoreAnchor: false,
                caretAlpha: caretAlphaNow,
                timeSeconds: timeSeconds,
                _visibleCommandIndicators,
                TakePendingMeshReleases(),
                focusedViewportBlockId: _focusedInlineViewportBlockId,
                selectedActionSpanBlockId: _selectedActionSpanBlockId,
                selectedActionSpanCommandText: _selectedActionSpanCommandText,
                selectedActionSpanIndex: _selectedActionSpanIndex,
                hasMousePosition: _hasMousePosition,
                mouseX: _lastMouseX,
                mouseY: _lastMouseY);

            _lastFrame = result.BackendFrame;
            _lastLayout = result.Layout;
            var rebuiltTicks = Stopwatch.GetTimestamp();
            _resizeCoordinator.NotifyFrameBuilt(framebufferWidth, framebufferHeight, result.BuiltGrid, rebuiltTicks);
            _lastCaretAlpha = caretAlphaNow;
            _lastSpinnerFrameIndex = ComputeSpinnerFrameIndex(rebuiltTicks);
            _lastCaretRenderTicks = rebuiltTicks;
            _pendingContentRebuild = false;
            ClearPendingMeshReleases();
        }

        MaybeUpdateOverlays(framebufferWidth, framebufferHeight, nowTicks, resizePlan.FramebufferSettled);

        if (_lastFrame is null)
        {
            throw new InvalidOperationException("Tick invariant violated: frame must be available.");
        }

        _scrollbarController.UpdateTotalRows(_lastLayout);
        var overlayFrame = _scrollbarController.BuildOverlayFrame(
            new PxRect(0, 0, framebufferWidth, framebufferHeight),
            _document.Settings.DefaultTextStyle.ForegroundRgba);

        var setVSync = _resizeCoordinator.ConsumeVSyncRequest();
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

        _caret.SetSuppressed(false, nowTicks);
        _caret.SetPromptFocused(IsPromptFocused(), nowTicks);
        _caret.Update(nowTicks);
        var caretAlpha = _caret.SampleAlpha(nowTicks);
        if (_document.Selection.ActiveRange is { } range && range.Anchor != range.Caret)
        {
            caretAlpha = 0;
            _caret.SetSuppressed(true, nowTicks);
        }
        if (caretAlpha == _lastCaretAlpha &&
            spinnerIndex == _lastSpinnerFrameIndex)
        {
            return;
        }

        var timeSeconds = nowTicks / (double)Stopwatch.Frequency;
        var renderFrame = _renderer.Render(
            _document,
            _lastLayout,
            _font,
            _selectionStyle,
            timeSeconds: timeSeconds,
            commandIndicators: _visibleCommandIndicators,
            caretAlpha: caretAlpha,
            meshReleases: TakePendingMeshReleases(),
            focusedViewportBlockId: _focusedInlineViewportBlockId,
            selectedActionSpanBlockId: _selectedActionSpanBlockId,
            selectedActionSpanCommandText: _selectedActionSpanCommandText,
            selectedActionSpanIndex: _selectedActionSpanIndex,
            hasMousePosition: _hasMousePosition,
            mouseX: _lastMouseX,
            mouseY: _lastMouseY);
        var backendFrame = RenderFrameAdapter.Adapt(renderFrame);
        ClearPendingMeshReleases();
        _lastFrame = backendFrame;
        _lastCaretAlpha = caretAlpha;
        _lastSpinnerFrameIndex = spinnerIndex;
        _lastCaretRenderTicks = nowTicks;
    }

    public void ResetTickClock(long nowTicks)
    {
        _lastTickTicks = nowTicks;
    }

    public void ClampTickDelta(long nowTicks, int maxDtMs)
    {
        if (_lastTickTicks == 0)
        {
            _lastTickTicks = nowTicks;
            return;
        }

        var maxDtTicks = (long)(maxDtMs * (Stopwatch.Frequency / 1000.0));
        var minLastTickTicks = nowTicks - maxDtTicks;
        if (_lastTickTicks < minLastTickTicks)
        {
            _lastTickTicks = minLastTickTicks;
        }
    }

    public long GetNextCaretDeadlineTicks() => _caret.NextDeadlineTicks;

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

    private void DrainPendingEvents(List<PendingEvent>? events, int framebufferWidth, int framebufferHeight, long nowTicks)
    {
        if (events is null || events.Count == 0)
        {
            return;
        }

        EnsureLayoutExists(framebufferWidth, framebufferHeight, nowTicks);
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is PendingEvent.FileDrop fileDrop)
            {
                ApplyCommandHostActions(_commandHost.HandleFileDrop(fileDrop.Path, _commandHostView));
                EnsureLayoutExists(framebufferWidth, framebufferHeight, nowTicks);
                _scrollbarController.UpdateTotalRows(_lastLayout);
                continue;
            }

            if (events[i] is PendingEvent.Text textInput && !char.IsControl(textInput.Ch))
            {
                if (_suppressNextTextInputTickIndex == _tickIndex)
                {
                    _suppressNextTextInputTickIndex = 0;
                    continue;
                }

                if (_focusedInlineViewportBlockId is not null)
                {
                    continue;
                }

                _caret.OnTyped(nowTicks);
                if (_selectedActionSpanIndex >= 0)
                {
                    ClearSelectedActionSpan();
                    _pendingContentRebuild = true;
                }
            }

            if (events[i] is PendingEvent.Key key && key.KeyCode != HostKey.Unknown)
            {
                if (key.IsDown &&
                    _focusedInlineViewportBlockId is null &&
                    key.KeyCode == HostKey.Backspace)
                {
                    _caret.OnTyped(nowTicks);
                }

                if (key.IsDown &&
                    (key.KeyCode == HostKey.Escape || key.KeyCode == HostKey.Q) &&
                    _focusedInlineViewportBlockId is not null)
                {
                    if (key.KeyCode == HostKey.Q)
                    {
                        _suppressNextTextInputTickIndex = _tickIndex;
                    }

                    SetFocusedInlineViewport(null, nowTicks);
                    _pendingContentRebuild = true;
                }
                else if (_focusedInlineViewportBlockId is not null)
                {
                    var handledFocusedKey = TryHandleFocusedInlineViewportKey(key, nowTicks);
                    if (handledFocusedKey)
                    {
                        _pendingContentRebuild = true;
                        continue;
                    }

                    if (_focusedInlineViewportBlockId is not null)
                    {
                        continue;
                    }
                }

                if (key.IsDown &&
                    _focusedInlineViewportBlockId is null &&
                    _selectedActionSpanIndex >= 0 &&
                    _lastLayout is not null &&
                    TryHandleSelectedActionSpanKey(key, _lastLayout))
                {
                    _pendingContentRebuild = true;
                    continue;
                }

                if (key.IsDown &&
                    (key.KeyCode == HostKey.PageUp || key.KeyCode == HostKey.PageDown) &&
                    _lastLayout is not null)
                {
                    var viewportRows = Math.Max(1, _lastLayout.Grid.Rows);
                    var delta = viewportRows - 1;
                    if (delta <= 0)
                    {
                        delta = 1;
                    }

                    if (key.KeyCode == HostKey.PageUp)
                    {
                        delta = -delta;
                    }

                    var cellH = _lastLayout.Grid.CellHeightPx;
                    var maxScrollOffsetRows = Math.Max(0, _lastLayout.TotalRows - _lastLayout.Grid.Rows);
                    var maxScrollOffsetPx = Math.Max(0, maxScrollOffsetRows * cellH);
                    var before = _document.Scroll.ScrollOffsetPx;
                    _document.Scroll.ApplyUserScrollDelta(delta * cellH, maxScrollOffsetPx);
                    if (_document.Scroll.ScrollOffsetPx != before)
                    {
                        _pendingContentRebuild = true;
                    }

                    continue;
                }
            }

            if (events[i] is PendingEvent.Mouse mouseRaw && _lastLayout is not null)
            {
                var mouseEvent = mouseRaw.Event;
                var viewportRectPx = new PxRect(0, 0, framebufferWidth, framebufferHeight);

                _lastMouseX = mouseEvent.X;
                _lastMouseY = mouseEvent.Y;
                _hasMousePosition = true;

                    if (mouseEvent.Kind == HostMouseEventKind.Down)
                    {
                        var scrollYPx = GetScrollOffsetPx(_document, _lastLayout);
                        if (TryHitTestInlineViewport(_lastLayout, mouseEvent.X, mouseEvent.Y, scrollYPx, out var hitViewport, out _))
                        {
                            SetFocusedInlineViewport(hitViewport.BlockId, nowTicks);
                        }
                    else
                    {
                        SetFocusedInlineViewport(null, nowTicks);
                    }
                }

                if (mouseEvent.Kind == HostMouseEventKind.Move)
                {
                    if (UpdateHoverAndCursor(mouseEvent.X, mouseEvent.Y, _lastLayout))
                    {
                        _pendingContentRebuild = true;
                    }
                }

                if (mouseEvent.Kind == HostMouseEventKind.Down &&
                    (mouseEvent.Buttons & HostMouseButtons.Left) != 0 &&
                    TryHandleActionSpanClick(mouseEvent, _lastLayout, nowTicks))
                {
                    _pendingContentRebuild = true;
                    continue;
                }

                if (TryHandleInlineViewportPointer(mouseEvent))
                {
                    _pendingContentRebuild = true;
                    continue;
                }

                if (_focusedInlineViewportBlockId is not null && mouseEvent.Kind == HostMouseEventKind.Wheel)
                {
                    continue;
                }

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
                EnsureLayoutExists(framebufferWidth, framebufferHeight, nowTicks);
                _pendingContentRebuild = false;
            }
        }
    }

    private void EnsureLayoutExists(int framebufferWidth, int framebufferHeight, long nowTicks)
    {
        if (_lastLayout is not null && _lastFrame is not null && !_pendingContentRebuild)
        {
            return;
        }

        UpdateVisibleCommandIndicators(nowTicks);
        _caret.SetSuppressed(false, nowTicks);
        _caret.SetPromptFocused(IsPromptFocused(), nowTicks);
        _caret.Update(nowTicks);
        var caretAlphaNow = _caret.SampleAlpha(nowTicks);
        if (_document.Selection.ActiveRange is { } range && range.Anchor != range.Caret)
        {
            caretAlphaNow = 0;
            _caret.SetSuppressed(true, nowTicks);
        }
        var timeSeconds = nowTicks / (double)Stopwatch.Frequency;
        FreezeTranscriptFollowTailWhileViewportFocused();
        var viewport = new ConsoleViewport(framebufferWidth, framebufferHeight);
        var result = _renderPipeline.BuildFrame(
            _document,
            _layoutSettings,
            viewport,
            restoreAnchor: false,
            caretAlpha: caretAlphaNow,
            timeSeconds: timeSeconds,
            _visibleCommandIndicators,
            TakePendingMeshReleases(),
            focusedViewportBlockId: _focusedInlineViewportBlockId,
            selectedActionSpanBlockId: _selectedActionSpanBlockId,
            selectedActionSpanCommandText: _selectedActionSpanCommandText,
            selectedActionSpanIndex: _selectedActionSpanIndex,
            hasMousePosition: _hasMousePosition,
            mouseX: _lastMouseX,
            mouseY: _lastMouseY);

        _lastFrame = result.BackendFrame;
        _lastLayout = result.Layout;
        _resizeCoordinator.NotifyFrameBuilt(framebufferWidth, framebufferHeight, result.BuiltGrid, nowTicks);
        _lastCaretAlpha = caretAlphaNow;
        _lastSpinnerFrameIndex = ComputeSpinnerFrameIndex(nowTicks);
        _lastCaretRenderTicks = nowTicks;
        ClearPendingMeshReleases();
    }

    private bool UpdateHoverAndCursor(int mouseX, int mouseY, LayoutFrame layout)
    {
        if (layout.Scrollbar.IsScrollable)
        {
            var sb = layout.Scrollbar;
            if ((mouseX >= sb.HitTrackRectPx.X &&
                 mouseY >= sb.HitTrackRectPx.Y &&
                 mouseX < sb.HitTrackRectPx.X + sb.HitTrackRectPx.Width &&
                 mouseY < sb.HitTrackRectPx.Y + sb.HitTrackRectPx.Height) ||
                mouseX >= sb.TrackRectPx.X)
            {
                var hadHover = _hoveredActionSpan is not null;
                _hoveredActionSpan = null;
                _hoveredActionSpanIndex = -1;
                _cursorKind = HostCursorKind.Default;
                return hadHover;
            }
        }

        var scrollYPx = GetScrollOffsetPx(_document, layout);
        var adjustedY = mouseY + scrollYPx;

        HostCursorKind cursor;

        var hoveredIndex = -1;
        if (TryGetActionSpanIndexOnRow(layout, mouseX, adjustedY, out hoveredIndex) &&
            hoveredIndex >= 0 &&
            hoveredIndex < layout.HitTestMap.ActionSpans.Count)
        {
            var span = layout.HitTestMap.ActionSpans[hoveredIndex];
            cursor = HostCursorKind.Default;

            var hoverChanged = _hoveredActionSpanIndex != hoveredIndex || _hoveredActionSpan != span;
            _hoveredActionSpan = span;
            _hoveredActionSpanIndex = hoveredIndex;
            _cursorKind = cursor;
            return hoverChanged;
        }

        var clearedHover = _hoveredActionSpan is not null;
        _hoveredActionSpan = null;
        _hoveredActionSpanIndex = -1;

        cursor = HostCursorKind.Default;
        if (layout.HitTestMap.Lines.Count > 0)
        {
            var hit = new HitTester().HitTest(layout.HitTestMap, mouseX, adjustedY);
            if (hit is { } pos && TryGetBlockById(pos.BlockId, out var block))
            {
                if (block is PromptBlock ||
                    (block is ITextSelectable selectable && selectable.CanSelect))
                {
                    cursor = HostCursorKind.IBeam;
                }
            }
        }

        _cursorKind = cursor;
        return clearedHover;
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

        var adjustedX = e.X;
        var adjustedY = e.Y + GetScrollOffsetPx(_document, _lastLayout);

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

    private void SetFocusedInlineViewport(BlockId? blockId, long nowTicks)
    {
        if (_focusedInlineViewportBlockId == blockId)
        {
            return;
        }

        if (_focusedInlineViewportBlockId is { } previous && TryGetBlockById(previous, out var previousBlock))
        {
            if (previousBlock is IMouseFocusableViewportBlock focusable)
            {
                focusable.HasMouseFocus = false;
            }
        }

        _focusedInlineViewportBlockId = blockId;
        _focusedScene3DNavKeysDown = Scene3DNavKeys.None;

        FreezeTranscriptFollowTailWhileViewportFocused();

        _caret.SetSuppressed(false, nowTicks);
        _caret.SetPromptFocused(IsPromptFocused(), nowTicks);
        _caret.Update(nowTicks);

        if (_focusedInlineViewportBlockId is { } next && TryGetBlockById(next, out var nextBlock))
        {
            if (nextBlock is IMouseFocusableViewportBlock focusable)
            {
                focusable.HasMouseFocus = true;
            }
        }

        _pendingContentRebuild = true;
    }

    private bool IsPromptFocused()
    {
        if (!_windowIsFocused)
        {
            return false;
        }

        if (_inspectController.IsActive)
        {
            return false;
        }

        return _focusedInlineViewportBlockId is null;
    }

    private static bool HasTypingTrigger(List<PendingEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is PendingEvent.Text text && !char.IsControl(text.Ch))
            {
                return true;
            }

            if (events[i] is PendingEvent.Key key && key.IsDown && key.KeyCode != HostKey.Unknown)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryHandleFocusedInlineViewportKey(in PendingEvent.Key key, long nowTicks)
    {
        if (_focusedInlineViewportBlockId is not { } focusedId ||
            !TryGetBlockById(focusedId, out var block))
        {
            SetFocusedInlineViewport(null, nowTicks);
            return false;
        }

        if ((key.Mods & (HostKeyModifiers.Control | HostKeyModifiers.Alt)) == 0 &&
            block is IScene3DViewBlock &&
            TryHandleFocusedScene3DNavKey(key.KeyCode, key.IsDown))
        {
            return true;
        }

        if (key.IsDown &&
            (key.Mods & HostKeyModifiers.Control) != 0 &&
            block is IBlockTextSelection selection)
        {
            if (key.KeyCode == HostKey.C)
            {
                if (selection.TryGetSelectedText(out var selected) && selected.Length > 0)
                {
                    _clipboard.SetText(selected);
                    return true;
                }
            }
            else if (key.KeyCode == HostKey.A)
            {
                selection.SelectAll();
                return true;
            }
        }

        if (block is not IBlockKeyHandler keyHandler)
        {
            return false;
        }

        var handled = keyHandler.HandleKey(new HostKeyEvent(key.KeyCode, key.Mods, key.IsDown));
        return handled;
    }

    private bool TryHandleFocusedScene3DNavKey(HostKey key, bool isDown)
    {
        var mask = key switch
        {
            HostKey.W => Scene3DNavKeys.W,
            HostKey.A => Scene3DNavKeys.A,
            HostKey.S => Scene3DNavKeys.S,
            HostKey.D => Scene3DNavKeys.D,
            _ => Scene3DNavKeys.None
        };

        if (mask == Scene3DNavKeys.None)
        {
            return false;
        }

        if (isDown)
        {
            _focusedScene3DNavKeysDown |= mask;
        }
        else
        {
            _focusedScene3DNavKeysDown &= ~mask;
        }

        return true;
    }

    private bool TickFocusedScene3DKeys(TimeSpan dt)
    {
        if (_focusedInlineViewportBlockId is null)
        {
            _focusedScene3DNavKeysDown = Scene3DNavKeys.None;
            return false;
        }

        if (_focusedScene3DNavKeysDown == Scene3DNavKeys.None)
        {
            return false;
        }

        if (!TryGetBlockById(_focusedInlineViewportBlockId.Value, out var block) ||
            block is not IScene3DViewBlock stl)
        {
            _focusedScene3DNavKeysDown = Scene3DNavKeys.None;
            return false;
        }

        var dtSeconds = (float)dt.TotalSeconds;
        if (dtSeconds <= 0f)
        {
            return false;
        }

        var settings = _document.Settings.Scene3D;

        var pan = 0f;
        if ((_focusedScene3DNavKeysDown & Scene3DNavKeys.D) != 0) pan += 1f;
        if ((_focusedScene3DNavKeysDown & Scene3DNavKeys.A) != 0) pan -= 1f;

        var dolly = 0f;
        if ((_focusedScene3DNavKeysDown & Scene3DNavKeys.W) != 0) dolly += 1f;
        if ((_focusedScene3DNavKeysDown & Scene3DNavKeys.S) != 0) dolly -= 1f;

        var didAnything = false;
        var (right, _, _) = Scene3DPointerController.ComputeSceneBasis(stl.CenterDir);

        if (pan != 0f)
        {
            var scale = stl.FocusDistance * settings.KeyboardPanSpeed * dtSeconds;
            var sign = settings.InvertPanX ? -1f : 1f;
            var delta = (pan * scale * sign) * right;

            if (stl is IScene3DOrbitBlock orbit && orbit.NavigationMode == Scene3DNavigationMode.Orbit)
            {
                orbit.OrbitTarget += delta;
                stl.CameraPos += delta;
            }
            else
            {
                stl.CameraPos += delta;
            }

            didAnything = true;
        }

        if (dolly != 0f)
        {
            var exponent = dolly * settings.KeyboardDollySpeed * dtSeconds;
            var factor = MathF.Exp(-exponent);
            Scene3DPointerController.ApplySceneDollyFactor(stl, factor);
            didAnything = true;
        }

        return didAnything;
    }

    private bool TryGetBlockById(BlockId id, out IBlock block)
    {
        var blocks = _document.Transcript.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            var candidate = blocks[i];
            if (candidate.Id == id)
            {
                block = candidate;
                return true;
            }
        }

        block = null!;
        return false;
    }

    [Flags]
    private enum Scene3DNavKeys
    {
        None = 0,
        W = 1 << 0,
        A = 1 << 1,
        S = 1 << 2,
        D = 1 << 3
    }

    private bool TryHandleInlineViewportPointer(in HostMouseEvent e)
    {
        if (_lastLayout is null)
        {
            return false;
        }

        var scrollYPx = GetScrollOffsetPx(_document, _lastLayout);

        if (_capturedInlineViewportBlockId is { } capturedId)
        {
            if (!TryGetInlineViewportByBlockId(_lastLayout, capturedId, scrollYPx, out var viewport, out var viewportRectPx) ||
                !TryGetInlineViewportBlock(viewport, out var block))
            {
                _capturedInlineViewportBlockId = null;
                return false;
            }

            _ = DispatchInlineViewportPointer(block, viewportRectPx, e);
            UpdateInlineViewportCapture(block, viewport.BlockId, e);
            return true;
        }

        if (e.Kind is HostMouseEventKind.Move or HostMouseEventKind.Up)
        {
            return false;
        }

        if (!TryHitTestInlineViewport(_lastLayout, e.X, e.Y, scrollYPx, out var hitViewport, out var hitViewportRectPx) ||
            !TryGetInlineViewportBlock(hitViewport, out var hitBlock))
        {
            return false;
        }

        var isInsideContentRect =
            e.X >= hitViewportRectPx.X &&
            e.Y >= hitViewportRectPx.Y &&
            e.X < hitViewportRectPx.X + hitViewportRectPx.Width &&
            e.Y < hitViewportRectPx.Y + hitViewportRectPx.Height;

        if (e.Kind == HostMouseEventKind.Wheel)
        {
            if (_focusedInlineViewportBlockId != hitViewport.BlockId)
            {
                return false;
            }

            return DispatchInlineViewportPointer(hitBlock, hitViewportRectPx, e);
        }

        var allowOutsideContentRect = hitBlock is not IScene3DViewBlock;
        var shouldDispatch = isInsideContentRect || allowOutsideContentRect;
        var hitConsumed = shouldDispatch && DispatchInlineViewportPointer(hitBlock, hitViewportRectPx, e);

        if (e.Kind == HostMouseEventKind.Down)
        {
            if (hitBlock is IScene3DViewBlock)
            {
                if (isInsideContentRect)
                {
                    _capturedInlineViewportBlockId = _inlineScene3DPointer.CapturedBlockId;
                }
            }
            else if (hitBlock is IBlockPointerCaptureState captureState && captureState.HasPointerCapture)
            {
                _capturedInlineViewportBlockId = hitViewport.BlockId;
            }

            UpdateInlineViewportCapture(hitBlock, hitViewport.BlockId, e);
            return true;
        }

        UpdateInlineViewportCapture(hitBlock, hitViewport.BlockId, e);
        return hitConsumed;
    }

    private bool DispatchInlineViewportPointer(IBlock block, in PxRect viewportRectPx, in HostMouseEvent e)
    {
        if (block is IBlockWheelHandler wheelHandler && e.Kind == HostMouseEventKind.Wheel)
        {
            return wheelHandler.HandleWheel(e, viewportRectPx);
        }

        if (block is IBlockPointerHandler pointerHandler &&
            e.Kind is HostMouseEventKind.Down or HostMouseEventKind.Up or HostMouseEventKind.Move)
        {
            if (e.Kind == HostMouseEventKind.Move)
            {
                return pointerHandler.HandlePointer(e, viewportRectPx);
            }

            _ = pointerHandler.HandlePointer(e, viewportRectPx);
            return true;
        }

        if (block is IScene3DViewBlock stl)
        {
            return _inlineScene3DPointer.Handle(stl, viewportRectPx, e, _document.Settings.Scene3D);
        }

        return false;
    }

    private void UpdateInlineViewportCapture(IBlock block, BlockId blockId, in HostMouseEvent e)
    {
        if (e.Kind == HostMouseEventKind.Up)
        {
            _capturedInlineViewportBlockId = null;
            return;
        }

        if (block is IBlockPointerCaptureState captureState &&
            _capturedInlineViewportBlockId == blockId &&
            !captureState.HasPointerCapture)
        {
            _capturedInlineViewportBlockId = null;
        }
    }

    private static bool TryGetInlineViewportByBlockId(
        LayoutFrame layout,
        BlockId blockId,
        int scrollYPx,
        out Scene3DViewportLayout viewport,
        out PxRect viewportRectPx)
    {
        var viewports = layout.Scene3DViewports;
        for (var i = 0; i < viewports.Count; i++)
        {
            var candidate = viewports[i];
            if (candidate.BlockId != blockId)
            {
                continue;
            }

            viewport = candidate;
            var rect = candidate.InnerViewportRectPx;
            viewportRectPx = new PxRect(rect.X, rect.Y - scrollYPx, rect.Width, rect.Height);
            return true;
        }

        viewport = default;
        viewportRectPx = default;
        return false;
    }

    private static bool TryHitTestInlineViewport(
        LayoutFrame layout,
        int x,
        int y,
        int scrollYPx,
        out Scene3DViewportLayout viewport,
        out PxRect viewportRectPx)
    {
        var viewports = layout.Scene3DViewports;
        for (var i = 0; i < viewports.Count; i++)
        {
            var candidate = viewports[i];
            var rect = candidate.ViewportRectPx;
            var screenRect = new PxRect(rect.X, rect.Y - scrollYPx, rect.Width, rect.Height);

            if (x < screenRect.X ||
                y < screenRect.Y ||
                x >= screenRect.X + screenRect.Width ||
                y >= screenRect.Y + screenRect.Height)
            {
                continue;
            }

            viewport = candidate;
            var innerRect = candidate.InnerViewportRectPx;
            viewportRectPx = new PxRect(innerRect.X, innerRect.Y - scrollYPx, innerRect.Width, innerRect.Height);
            return true;
        }

        viewport = default;
        viewportRectPx = default;
        return false;
    }

    private bool TryGetInlineViewportBlock(in Scene3DViewportLayout viewport, out IBlock block)
    {
        var blockIndex = viewport.BlockIndex;
        var blocks = _document.Transcript.Blocks;
        if (blockIndex < 0 || blockIndex >= blocks.Count)
        {
            block = null!;
            return false;
        }

        block = blocks[blockIndex];
        return block.Id == viewport.BlockId;
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
                case HostAction.SetPromptInput setPromptInput:
                    if (TryGetPrompt(setPromptInput.PromptId, out var updatePrompt))
                    {
                        updatePrompt.Input = setPromptInput.Input ?? string.Empty;
                        updatePrompt.SetCaret(setPromptInput.CaretIndex);
                        _document.Scroll.IsFollowingTail = true;
                        _pendingContentRebuild = true;
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
                    HandlePromptSubmit(submit.PromptId);
                    _document.Scroll.IsFollowingTail = true;
                    break;
                case HostAction.NavigateHistory nav:
                    ApplyCommandHostActions(_commandHost.HandleNavigateHistory(nav.PromptId, nav.Delta, _commandHostView));
                    break;
                case HostAction.Autocomplete ac:
                    ApplyCommandHostActions(_commandHost.HandleAutocomplete(ac.PromptId, ac.Delta, _commandHostView));
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

            _commandHost.NotifyHostActionApplied(action);
        }

        _document.Selection.ActiveRange = _interaction.Snapshot.Selection;
    }

    private void ApplyCommandHostActions(IReadOnlyList<CommandHostAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            switch (actions[i])
            {
                case CommandHostAction.InsertTextBlockBefore insert:
                    InsertBlockBefore(insert.BeforeId, new TextBlock(insert.NewId, insert.Text, insert.Stream));
                    _pendingContentRebuild = true;
                    break;
                case CommandHostAction.InsertTextBlockAfter insert:
                    InsertBlockAfter(insert.AfterId, new TextBlock(insert.NewId, insert.Text, insert.Stream));
                    _pendingContentRebuild = true;
                    break;
                case CommandHostAction.UpdatePrompt update:
                    if (TryGetPrompt(update.PromptId, out var prompt))
                    {
                        prompt.Input = update.Input ?? string.Empty;
                        prompt.SetCaret(update.CaretIndex);
                        _pendingContentRebuild = true;
                    }
                    break;
                case CommandHostAction.SubmitParsedCommand submit:
                    ApplyParsedCommand(submit);
                    break;
                case CommandHostAction.RequestContentRebuild:
                    _pendingContentRebuild = true;
                    break;
                case CommandHostAction.SetFollowingTail tail:
                    _document.Scroll.IsFollowingTail = tail.Enabled;
                    break;
            }
        }
    }

    private void ApplyInspectActions(IReadOnlyList<InspectAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            switch (actions[i])
            {
                case InspectRequestContentRebuild:
                    _pendingContentRebuild = true;
                    break;
                case InspectHandleFileDrop drop:
                    HandleFileDropFromInspect(drop.Path);
                    break;
                case InspectWriteReceipt receipt:
                {
                    if (string.IsNullOrWhiteSpace(receipt.ReceiptLine))
                    {
                        break;
                    }

                    var receiptId = new BlockId(AllocateNewBlockId());
                    var blocks = _document.Transcript.Blocks;
                    var insertAt = blocks.Count;
                    for (var b = 0; b < blocks.Count; b++)
                    {
                        if (blocks[b].Id == receipt.CommandEchoId)
                        {
                            insertAt = b + 1;
                            break;
                        }
                    }

                    insertAt = Math.Clamp(insertAt, 0, blocks.Count);
                    _document.Transcript.Insert(insertAt, new TextBlock(receiptId, receipt.ReceiptLine, ConsoleTextStream.System));
                    _pendingContentRebuild = true;
                    break;
                }
            }
        }
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

    private void HandlePromptSubmit(BlockId promptId)
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
            ApplyCommandHostActions(_commandHost.HandleSubmitPrompt(promptId, _commandHostView));
        }
    }

    private void ApplyParsedCommand(CommandHostAction.SubmitParsedCommand submit)
    {
        var submitResult = _commandSubmission.SubmitParsed(
            submit.Request,
            submit.CommandForParse,
            submit.HeaderId,
            submit.ShellPromptId,
            this);

        if (submitResult.IsParseFailed)
        {
            return;
        }

        if (submitResult.Handled)
        {
            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;

            if (submitResult.StartedBlockingActivity)
            {
                RemoveShellPromptIfPresent(submit.ShellPromptId);
                _promptLifecycle.PendingShellPrompt = true;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(submit.RawCommand))
        {
            InsertBlockAfter(
                submit.HeaderId,
                new TextBlock(new BlockId(AllocateNewBlockId()), "Unrecognized command.", ConsoleTextStream.System));
            _document.Scroll.IsFollowingTail = true;
            _pendingContentRebuild = true;
        }
    }

    private void HandleFileDropFromInspect(string path)
    {
        EnsureShellPromptForFileDrop();
        ApplyCommandHostActions(_commandHost.HandleFileDrop(path, _commandHostView));
    }

    private void EnsureShellPromptForFileDrop()
    {
        var prompt = FindLastPrompt(_document.Transcript);
        if (prompt is not null)
        {
            return;
        }

        _document.Transcript.Add(new PromptBlock(new BlockId(AllocateNewBlockId()), _defaultPromptText));
        _promptLifecycle.PendingShellPrompt = false;
        _pendingContentRebuild = true;
    }

    private void ClearTranscript()
    {
        _commandHost.ResetOnClear();
        QueueMeshReleasesForAllBlocks();
        _document.Transcript.Clear();
        _document.Transcript.Add(new PromptBlock(new BlockId(AllocateNewBlockId()), _defaultPromptText));

        _document.Selection.ActiveRange = null;
        _interaction.Initialize(_document.Transcript);
        _capturedInlineViewportBlockId = null;
        _focusedInlineViewportBlockId = null;
        _focusedScene3DNavKeysDown = Scene3DNavKeys.None;
        _hoveredActionSpanIndex = -1;
        _hoveredActionSpan = null;
        ClearSelectedActionSpan();
        SetCaretToEndOfLastPrompt(_document.Transcript);
        UpdateShellPromptTextIfPresent();

        _jobProjectionService.Clear();
        _promptLifecycle.Clear();
        _commandIndicators.Clear();
        _commandIndicatorStartTicks.Clear();
        _visibleCommandIndicators.Clear();

        _document.Scroll.ScrollOffsetPx = 0;
        _document.Scroll.IsFollowingTail = true;
        _document.Scroll.ScrollPxFromBottom = 0;
        _document.Scroll.TopVisualLineAnchor = null;
        _document.Scroll.ScrollbarUi.Visibility = 0;
        _document.Scroll.ScrollbarUi.IsHovering = false;
        _document.Scroll.ScrollbarUi.IsDragging = false;
        _document.Scroll.ScrollbarUi.MsSinceInteraction = 0;
        _document.Scroll.ScrollbarUi.DragGrabOffsetYPx = 0;

        _pendingContentRebuild = true;

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
            UpdateShellPromptTextIfPresent();
            _promptLifecycle.PendingShellPrompt = false;
            return;
        }

        if (!_promptLifecycle.PendingShellPrompt && blocks.Count > 0)
        {
            return;
        }

        _promptLifecycle.EnsureShellPromptAtEndIfNeeded(AllocateNewBlockId);
        UpdateShellPromptTextIfPresent();
        _pendingContentRebuild = true;
    }

    private void InitializeWorkingDirectory()
    {
        var dir = Directory.GetCurrentDirectory();
        TrySetCurrentDirectory(dir, out _);
    }

    private void InitializeHomeDirectory()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                _homeDirectory = Path.GetFullPath(home);
                return;
            }
        }
        catch
        {
        }

        try
        {
            _homeDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
        }
        catch
        {
            _homeDirectory = string.Empty;
        }
    }

    private bool TrySetCurrentDirectory(string directory, out string error)
    {
        error = string.Empty;
        directory ??= string.Empty;

        string full;
        try
        {
            full = ConsolePathResolver.ResolvePath(_currentDirectory, _lastDirectoryPerDrive, directory);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!_fileSystem.DirectoryExists(full))
        {
            error = $"Directory not found: {full}";
            return false;
        }

        try
        {
            Directory.SetCurrentDirectory(full);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        _currentDirectory = full;
        if (TryGetDriveLetter(full, out var drive))
        {
            _lastDirectoryPerDrive[char.ToUpperInvariant(drive)] = full;
        }

        UpdateShellPromptTextIfPresent();
        _pendingContentRebuild = true;
        return true;
    }

    private void UpdateShellPromptTextIfPresent()
    {
        var blocks = _document.Transcript.Blocks;
        if (blocks.Count == 0 || blocks[^1] is not PromptBlock prompt || prompt.Owner is not null)
        {
            return;
        }

        prompt.Prompt = BuildShellPrompt(_currentDirectory);
    }

    private static string BuildShellPrompt(string currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(currentDirectory))
        {
            return "> ";
        }

        var trimmed = currentDirectory.Trim();
        try
        {
            var root = Path.GetPathRoot(trimmed);
            if (!string.IsNullOrEmpty(root))
            {
                var rootFull = Path.GetFullPath(root);
                var dirFull = Path.GetFullPath(trimmed);
                if (StringComparer.OrdinalIgnoreCase.Equals(rootFull, dirFull))
                {
                    return rootFull + "> ";
                }
            }
        }
        catch
        {
        }

        return trimmed.TrimEnd('\\', '/') + "> ";
    }

    private static bool TryGetDriveLetter(string path, out char driveLetter)
    {
        driveLetter = default;
        if (string.IsNullOrEmpty(path) || path.Length < 2 || path[1] != ':')
        {
            return false;
        }

        var ch = path[0];
        if (!char.IsLetter(ch))
        {
            return false;
        }

        driveLetter = ch;
        return true;
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

    private sealed class CommandHostViewAdapter : ICommandHostView
    {
        private readonly ConsoleHostSession _session;

        public CommandHostViewAdapter(ConsoleHostSession session)
        {
            _session = session;
        }

        public bool TryGetPromptSnapshot(BlockId promptId, out PromptSnapshot prompt)
        {
            if (_session.TryGetPrompt(promptId, out var block))
            {
                prompt = new PromptSnapshot(block.Id, block.Prompt, block.Input, block.CaretIndex, block.Owner);
                return true;
            }

            prompt = default;
            return false;
        }

        public PromptSnapshot? GetLastPromptSnapshot()
        {
            var prompt = FindLastPrompt(_session._document.Transcript);
            if (prompt is null)
            {
                return null;
            }

            return new PromptSnapshot(prompt.Id, prompt.Prompt, prompt.Input, prompt.CaretIndex, prompt.Owner);
        }

        public BlockId AllocateBlockId() => new(_session.AllocateNewBlockId());
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

    private bool TryGetLastEditablePromptId(out BlockId promptId)
    {
        var prompt = FindLastPrompt(_document.Transcript);
        if (prompt is null || prompt.Owner is not null)
        {
            promptId = default;
            return false;
        }

        promptId = prompt.Id;
        return true;
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




    private static int GetScrollOffsetPx(ConsoleDocument document, LayoutFrame layout)
    {
        var maxScrollOffsetRows = layout.Grid.Rows <= 0
            ? 0
            : Math.Max(0, layout.TotalRows - layout.Grid.Rows);

        var cellH = layout.Grid.CellHeightPx;
        var maxScrollOffsetPx = Math.Max(0, maxScrollOffsetRows * cellH);
        return Math.Clamp(document.Scroll.ScrollOffsetPx, 0, maxScrollOffsetPx);
    }

    private static long GetDoubleClickTicks() => (long)(Stopwatch.Frequency * 0.35);

    private static bool IsFileEntryCommand(string commandText)
    {
        return IsCdCommand(commandText) || commandText.StartsWith("view ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCdCommand(string commandText) => commandText.StartsWith("cd ", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetActionSpanIndexOnRow(LayoutFrame layout, int pixelX, int pixelY, out int spanIndex)
    {
        var map = layout.HitTestMap;
        if (map.TryGetActionAt(pixelX, pixelY, out spanIndex))
        {
            return true;
        }

        spanIndex = -1;
        var grid = layout.Grid;
        var cellH = grid.CellHeightPx;
        if (cellH <= 0)
        {
            return false;
        }

        var localY = pixelY - grid.PaddingTopPx;
        if (localY < 0)
        {
            return false;
        }

        var row = localY / cellH;
        var rowY = grid.PaddingTopPx + (row * cellH);

        var best = -1;
        var bestDist = int.MaxValue;
        var spans = map.ActionSpans;
        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            var r = span.RectPx;
            if (r.Y > rowY)
            {
                break;
            }

            if (r.Y != rowY)
            {
                continue;
            }

            var dist = 0;
            if (pixelX < r.X) dist = r.X - pixelX;
            else if (pixelX >= r.X + r.Width) dist = pixelX - (r.X + r.Width);

            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
                if (dist == 0)
                {
                    break;
                }
            }
        }

        if (best >= 0)
        {
            spanIndex = best;
            return true;
        }

        return false;
    }

    private void ClearSelectedActionSpan()
    {
        _selectedActionSpanIndex = -1;
        _selectedActionSpanBlockId = null;
        _selectedActionSpanCommandText = null;
        _lastActionSpanClickTicks = 0;
        _lastActionSpanClickIndex = -1;
        _lastActionSpanClickBlockId = null;
    }

    private bool TryHandleSelectedActionSpanKey(in PendingEvent.Key key, LayoutFrame layout)
    {
        if (_selectedActionSpanIndex < 0 ||
            _selectedActionSpanBlockId is null ||
            _selectedActionSpanCommandText is null)
        {
            return false;
        }

        if (key.KeyCode == HostKey.Escape)
        {
            ClearSelectedActionSpan();
            return true;
        }

        if (key.KeyCode == HostKey.Enter)
        {
            ActivateEntry(_selectedActionSpanCommandText);
            ClearSelectedActionSpan();
            return true;
        }

        var delta = key.KeyCode switch
        {
            HostKey.Up => -1,
            HostKey.Left => -1,
            HostKey.Down => 1,
            HostKey.Right => 1,
            _ => 0
        };

        if (delta == 0)
        {
            return false;
        }

        if (!TryMoveSelectedActionSpan(layout, delta))
        {
            return true;
        }

        EnsureSelectedActionSpanVisible(layout);
        return true;
    }

    private bool TryMoveSelectedActionSpan(LayoutFrame layout, int delta)
    {
        var spans = layout.HitTestMap.ActionSpans;
        if (_selectedActionSpanIndex < 0 || _selectedActionSpanIndex >= spans.Count || _selectedActionSpanBlockId is null)
        {
            ClearSelectedActionSpan();
            return false;
        }

        var blockId = _selectedActionSpanBlockId.Value;
        if (delta < 0)
        {
            for (var i = _selectedActionSpanIndex - 1; i >= 0; i--)
            {
                var span = spans[i];
                if (span.BlockId == blockId && IsFileEntryCommand(span.CommandText))
                {
                    _selectedActionSpanIndex = i;
                    _selectedActionSpanCommandText = span.CommandText;
                    return true;
                }
            }

            return false;
        }

        for (var i = _selectedActionSpanIndex + 1; i < spans.Count; i++)
        {
            var span = spans[i];
            if (span.BlockId == blockId && IsFileEntryCommand(span.CommandText))
            {
                _selectedActionSpanIndex = i;
                _selectedActionSpanCommandText = span.CommandText;
                return true;
            }
        }

        return false;
    }

    private void EnsureSelectedActionSpanVisible(LayoutFrame layout)
    {
        if (_selectedActionSpanIndex < 0 || _selectedActionSpanIndex >= layout.HitTestMap.ActionSpans.Count)
        {
            return;
        }

        var rect = layout.HitTestMap.ActionSpans[_selectedActionSpanIndex].RectPx;
        var grid = layout.Grid;
        if (grid.CellHeightPx <= 0)
        {
            return;
        }

        var scrollYPx = _document.Scroll.ScrollOffsetPx;
        var y0 = rect.Y - scrollYPx;
        var y1 = y0 + rect.Height;
        var viewH = grid.FramebufferHeightPx;

        if (y0 < 0)
        {
            _document.Scroll.ScrollOffsetPx = Math.Max(0, _document.Scroll.ScrollOffsetPx + y0);
            _document.Scroll.IsFollowingTail = false;
        }
        else if (y1 > viewH)
        {
            var maxScrollOffsetPx = Math.Max(0, (layout.TotalRows - grid.Rows) * grid.CellHeightPx);
            _document.Scroll.ScrollOffsetPx = Math.Min(maxScrollOffsetPx, _document.Scroll.ScrollOffsetPx + (y1 - viewH));
            _document.Scroll.IsFollowingTail = false;
        }
    }

    private bool TryHandleActionSpanClick(in HostMouseEvent mouseEvent, LayoutFrame layout, long nowTicks)
    {
        if (mouseEvent.Kind != HostMouseEventKind.Down || (mouseEvent.Buttons & HostMouseButtons.Left) == 0)
        {
            return false;
        }

        if ((mouseEvent.Mods & HostKeyModifiers.Shift) != 0)
        {
            return false;
        }

        if (layout.Scrollbar.IsScrollable)
        {
            var sb = layout.Scrollbar;
            if (mouseEvent.X >= sb.HitTrackRectPx.X &&
                mouseEvent.Y >= sb.HitTrackRectPx.Y &&
                mouseEvent.X < sb.HitTrackRectPx.X + sb.HitTrackRectPx.Width &&
                mouseEvent.Y < sb.HitTrackRectPx.Y + sb.HitTrackRectPx.Height)
            {
                return false;
            }

            if (mouseEvent.X >= sb.TrackRectPx.X)
            {
                return false;
            }
        }

        var adjustedY = mouseEvent.Y + GetScrollOffsetPx(_document, layout);

        if (!TryGetActionSpanIndexOnRow(layout, mouseEvent.X, adjustedY, out int spanIndex) ||
            spanIndex < 0 ||
            spanIndex >= layout.HitTestMap.ActionSpans.Count)
        {
            if (_selectedActionSpanIndex >= 0)
            {
                ClearSelectedActionSpan();
                _pendingContentRebuild = true;
                return false;
            }

            return false;
        }

        var span = layout.HitTestMap.ActionSpans[spanIndex];
        if (!IsFileEntryCommand(span.CommandText))
        {
            return false;
        }

        var isDoubleClick =
            _lastActionSpanClickBlockId == span.BlockId &&
            _lastActionSpanClickIndex == spanIndex &&
            nowTicks - _lastActionSpanClickTicks <= GetDoubleClickTicks();

        _lastActionSpanClickTicks = nowTicks;
        _lastActionSpanClickIndex = spanIndex;
        _lastActionSpanClickBlockId = span.BlockId;

        _selectedActionSpanIndex = spanIndex;
        _selectedActionSpanBlockId = span.BlockId;
        _selectedActionSpanCommandText = span.CommandText;

        if (isDoubleClick)
        {
            ActivateEntry(span.CommandText);
            ClearSelectedActionSpan();
        }

        return true;
    }

    private void ActivateEntry(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return;
        }

        ExecuteCommandLine(commandText);
    }

    private void ExecuteCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return;
        }

        EnsureShellPromptAtEndIfNeeded();
        if (!TryGetLastEditablePromptId(out var promptId) || !TryGetPrompt(promptId, out var prompt))
        {
            return;
        }

        prompt.Input = commandLine;
        prompt.CaretIndex = commandLine.Length;
        HandlePromptSubmit(promptId);
        _document.Scroll.IsFollowingTail = true;
        _pendingContentRebuild = true;
    }

    private void FreezeTranscriptFollowTailWhileViewportFocused()
    {
        if (_focusedInlineViewportBlockId is not null)
        {
            _document.Scroll.IsFollowingTail = false;
        }
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
        ApplyInspectActions(_inspectController.DrainActions());
    }

    void IBlockCommandSession.ClearTranscript() => ClearTranscript();

    void IBlockCommandSession.RequestExit()
    {
        _pendingExit = true;
        _pendingContentRebuild = true;
    }

    PromptCaretSettings IBlockCommandSession.GetPromptCaretSettings() => _caret.Settings;

    void IBlockCommandSession.SetPromptCaretSettings(in PromptCaretSettings settings)
    {
        _caret.SetSettings(settings);
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
