using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cycon.Backends.Abstractions;
using Cycon.Backends.Abstractions.Rendering;
using Cycon.Core;
using Cycon.Core.Fonts;
using Cycon.Core.Metrics;
using Cycon.Core.Scrolling;
using Cycon.Core.Selection;
using Cycon.Core.Settings;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Interaction;
using Cycon.Host.Input;
using Cycon.Host.Services;
using Cycon.Layout;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;
using Cycon.Rendering.Renderer;
using Cycon.Rendering.Styling;

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

    private ConsoleHostSession(string text, IClipboard clipboard, int resizeSettleMs, int rebuildThrottleMs)
    {
        _resizeSettleMs = resizeSettleMs;
        _rebuildThrottleMs = rebuildThrottleMs;

        _clipboard = clipboard;
        _document = CreateDocument(text);
        _interaction.Initialize(_document.Transcript);
        SetCaretToEndOfLastPrompt(_document.Transcript);
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
    }

    public static ConsoleHostSession CreateVga(string text, IClipboard clipboard, int resizeSettleMs = 80, int rebuildThrottleMs = 80)
    {
        return new ConsoleHostSession(text, clipboard, resizeSettleMs, rebuildThrottleMs);
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

        var caretAlphaNow = ComputeCaretAlpha(nowTicks);

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
                    caretAlphaNow);

                _lastFrame = frame;
                _renderedGrid = builtGrid;
                _lastLayout = layout;
                LogOnce(_atlasData, layout, renderFrame, snapW, snapH);
                _lastRebuildTicks = passNowTicks;
                _lastCaretAlpha = caretAlphaNow;
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

        if (_pendingContentRebuild)
        {
            var (frame, builtGrid, layout, renderFrame) = BuildFrameFor(
                framebufferWidth,
                framebufferHeight,
                restoreAnchor: false,
                caretAlpha: caretAlphaNow);

            _lastFrame = frame;
            _renderedGrid = builtGrid;
            _lastLayout = layout;
            LogOnce(_atlasData, layout, renderFrame, framebufferWidth, framebufferHeight);
            _lastRebuildTicks = Stopwatch.GetTimestamp();
            _lastCaretAlpha = caretAlphaNow;
            _lastCaretRenderTicks = _lastRebuildTicks;
            _pendingContentRebuild = false;
        }

        MaybeUpdateCaret(framebufferWidth, framebufferHeight, nowTicks, framebufferSettled);

        if (_lastFrame is null)
        {
            throw new InvalidOperationException("Tick invariant violated: frame must be available.");
        }

        var setVSync = _pendingSetVSync;
        _pendingSetVSync = null;
        return new FrameTickResult(framebufferWidth, framebufferHeight, _lastFrame, setVSync);
    }

    private void MaybeUpdateCaret(int framebufferWidth, int framebufferHeight, long nowTicks, bool framebufferSettled)
    {
        if (!framebufferSettled || _lastLayout is null)
        {
            return;
        }

        var elapsedSinceCaretMs = (nowTicks - _lastCaretRenderTicks) * 1000.0 / Stopwatch.Frequency;
        if (_lastCaretRenderTicks != 0 && elapsedSinceCaretMs < 33)
        {
            return;
        }

        var caretAlpha = ComputeCaretAlpha(nowTicks);
        if (caretAlpha == _lastCaretAlpha)
        {
            return;
        }

        var renderFrame = _renderer.Render(_document, _lastLayout, _font, _selectionStyle, caretAlpha);
        var backendFrame = RenderFrameAdapter.Adapt(renderFrame);
        _lastFrame = backendFrame;
        _renderedGrid = backendFrame.BuiltGrid;
        _lastCaretAlpha = caretAlpha;
        _lastCaretRenderTicks = nowTicks;
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
        var caretAlphaNow = ComputeCaretAlpha(nowTicks);
        var (frame, builtGrid, layout, renderFrame) = BuildFrameFor(
            framebufferWidth,
            framebufferHeight,
            restoreAnchor: false,
            caretAlpha: caretAlphaNow);

        _lastFrame = frame;
        _renderedGrid = builtGrid;
        _lastLayout = layout;
        LogOnce(_atlasData, layout, renderFrame, framebufferWidth, framebufferHeight);
        _lastRebuildTicks = nowTicks;
        _lastCaretAlpha = caretAlphaNow;
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
            HostMouseEventKind.Wheel =>
                new InputEvent.MouseWheel(adjustedX, adjustedY, e.WheelDelta, e.Mods),
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
                case HostAction.CopySelectionToClipboard:
                    if (_interaction.TryGetSelectedText(_document.Transcript, out var selected))
                    {
                        _clipboard.SetText(selected);
                    }
                    break;
                case HostAction.PasteFromClipboardIntoLastPrompt:
                    PasteIntoLastPrompt();
                    break;
                case HostAction.RequestRebuild:
                    _pendingContentRebuild = true;
                    break;
            }
        }

        _document.Selection.ActiveRange = _interaction.Snapshot.Selection;
    }

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
        var insertIndex = Math.Max(0, _document.Transcript.Blocks.Count - 1);
        _document.Transcript.Insert(insertIndex, new TextBlock(new BlockId(AllocateNewBlockId()), prompt.Prompt + command));

        if (!string.IsNullOrWhiteSpace(command))
        {
            _document.Transcript.Insert(insertIndex + 1, new TextBlock(new BlockId(AllocateNewBlockId()), "Unrecognized command."));
        }

        prompt.Input = string.Empty;
        prompt.SetCaret(0);
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
            byte caretAlpha)
    {
        var viewport = new ConsoleViewport(framebufferWidth, framebufferHeight);
        var layout = _layoutEngine.Layout(_document, _layoutSettings, viewport);
        if (restoreAnchor)
        {
            ScrollAnchoring.RestoreFromAnchor(_document.Scroll, layout);
        }

        var renderFrame = _renderer.Render(_document, layout, _font, _selectionStyle, caretAlpha: caretAlpha);
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
    }
}

public readonly record struct FrameTickResult(
    int FramebufferWidth,
    int FramebufferHeight,
    RenderFrame Frame,
    bool? SetVSync);
