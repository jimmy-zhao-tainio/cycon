using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cycon.Core.Transcript;
using Cycon.Host.Ai;
using Cycon.Host.Hosting;
using Cycon.Host.Input;
using Cycon.Host.Interaction;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Metrics;
using Cycon.Layout.Overlays;
using Cycon.Layout.Scrolling;

namespace Cycon.Host.Overlays;

internal sealed class OverlayManager
{
    private enum OverlayKind
    {
        None = 0,
        HelpControls,
        AiApiKey,
        InputDemo
    }

    private OverlaySlab? _slab;
    private OverlaySlabFrame? _frame;
    private FixedCellGrid _lastGrid;
    private bool _hasGrid;
    private readonly UIActionRouter _actions = new();
    private readonly Func<string?> _getClipboardText;
    private readonly Action<string> _setClipboardText;
    private OverlayKind _kind;
    private OverlayTextInput? _textInput;
    private UIActionId _textInputId;
    private string _textInputError = string.Empty;

    public OverlayManager(Func<string?> getClipboardText, Action<string>? setClipboardText = null)
    {
        _getClipboardText = getClipboardText ?? (() => null);
        _setClipboardText = setClipboardText ?? (_ => { });
    }

    public int Version { get; private set; }

    public bool IsOpen => _slab is not null;

    public bool IsModal => _slab?.IsModal ?? false;

    public UIActionState ActionState => _actions.State;

    public bool ClearAllActions() => _actions.ClearAll();

    public long GetNextCaretDeadlineTicks()
    {
        if (_textInput is null || !_textInput.HasFocus)
        {
            return long.MaxValue;
        }

        return _textInput.NextDeadlineTicks;
    }

    public void Tick(long nowTicks)
    {
        if (_slab is null || _textInput is null)
        {
            return;
        }

        if (_textInput.Tick(nowTicks))
        {
            _frame = null;
            Version++;
        }
    }

    public void Close()
    {
        if (_slab is null)
        {
            return;
        }

        _slab = null;
        _frame = null;
        _hasGrid = false;
        _kind = OverlayKind.None;
        _textInput = null;
        _textInputId = default;
        _textInputError = string.Empty;
        _ = _actions.ClearAll();
        Version++;
    }

    public void OpenHelpControls(in FixedCellGrid grid)
    {
        _slab = CreateHelpControlsSlab();
        _kind = OverlayKind.HelpControls;
        _frame = null;
        _lastGrid = grid;
        _hasGrid = true;
        _ = _actions.ClearAll();
        _textInput = null;
        _textInputId = default;
        _textInputError = string.Empty;
        Version++;
    }

    public void OpenAiApiKey(in FixedCellGrid grid)
    {
        var now = Stopwatch.GetTimestamp();
        var initial = OpenAiApiKeyStore.TryGetApiKey() ?? string.Empty;
        _slab = CreateAiApiKeySlab();
        _kind = OverlayKind.AiApiKey;
        _frame = null;
        _lastGrid = grid;
        _hasGrid = true;
        _ = _actions.ClearAll();
        _textInput = new OverlayTextInput();
        _textInput.SetText(initial);
        _textInputId = new UIActionId(10);
        _textInputError = string.Empty;
        _ = _actions.SetFocus(_textInputId);
        _textInput.SetFocus(true, now);
        Version++;
    }

    public void OpenInputDemo(in FixedCellGrid grid)
    {
        var now = Stopwatch.GetTimestamp();
        _slab = CreateInputDemoSlab();
        _kind = OverlayKind.InputDemo;
        _frame = null;
        _lastGrid = grid;
        _hasGrid = true;
        _ = _actions.ClearAll();
        _textInput = new OverlayTextInput();
        _textInput.SetText(string.Empty);
        _textInputId = new UIActionId(10);
        _textInputError = string.Empty;
        _ = _actions.SetFocus(_textInputId);
        _textInput.SetFocus(true, now);
        Version++;
    }

    public OverlaySlabFrame? GetFrame(in FixedCellGrid grid)
    {
        if (_slab is null)
        {
            return null;
        }

        if (!_hasGrid ||
            _lastGrid.CellWidthPx != grid.CellWidthPx ||
            _lastGrid.CellHeightPx != grid.CellHeightPx ||
            _lastGrid.Cols != grid.Cols ||
            _lastGrid.Rows != grid.Rows ||
            _lastGrid.PaddingLeftPx != grid.PaddingLeftPx ||
            _lastGrid.PaddingTopPx != grid.PaddingTopPx ||
            _lastGrid.PaddingRightPx != grid.PaddingRightPx ||
            _lastGrid.PaddingBottomPx != grid.PaddingBottomPx ||
            _lastGrid.FramebufferWidthPx != grid.FramebufferWidthPx ||
            _lastGrid.FramebufferHeightPx != grid.FramebufferHeightPx)
        {
            _lastGrid = grid;
            _hasGrid = true;
            _frame = null;
        }

        _frame ??= BuildFrame(_slab, grid);
        return _frame;
    }

    public bool HandleKey(in PendingEvent.Key key, long nowTicks)
    {
        if (_slab is null)
        {
            return false;
        }

        // Esc is a global cancel for overlays: close immediately and discard any uncommitted edits.
        // Must win over focused text inputs and selection/drag states.
        if (key.IsDown && key.KeyCode == HostKey.Escape)
        {
            Close();
            return true;
        }

        if (_textInput is not null &&
            _actions.State.FocusedId is { } fid &&
            fid == _textInputId)
        {
            if (_hasGrid)
            {
                var frame = GetFrame(_lastGrid);
                if (frame?.TextInput is { } input)
                {
                    var outer = input.OuterRectPx;
                    var cellW = Math.Max(1, _lastGrid.CellWidthPx);
                    var textStartX = outer.X + (cellW * 3);
                    var innerWidth = Math.Max(0, outer.Width - (cellW * 6));
                    if (_textInput.OnKeyDown(
                            key,
                            _getClipboardText,
                            _setClipboardText,
                            _lastGrid,
                            textStartXPx: textStartX,
                            innerWidthPx: innerWidth,
                            nowTicks: nowTicks,
                            out var submitted))
                    {
                        _frame = null;
                        Version++;
                        if (submitted)
                        {
                            SubmitTextInput();
                        }
                        return true;
                    }
                }
            }
        }

        // Route editing/navigation keys to the focused text input (if any).
        if (key.KeyCode == HostKey.Enter)
        {
            if (!_hasGrid)
            {
                _ = _actions.ClearPressed();
                return true;
            }

            var frame = GetFrame(_lastGrid);
            if (frame is null)
            {
                _ = _actions.ClearPressed();
                return true;
            }

            if (key.IsDown)
            {
                if (_actions.State.FocusedId is { } focused)
                {
                    _ = _actions.SetPressed(focused);
                }

                return true;
            }

            // Key up: activate if still focused.
            if (_actions.State.PressedId is { } pressed &&
                _actions.State.FocusedId is { } focusedUp &&
                pressed == focusedUp &&
                TryFindActionById(frame.Actions, focusedUp, out var action) &&
                action.Enabled)
            {
                Activate(action);
            }

            _ = _actions.ClearPressed();
            return true;
        }

        if (!key.IsDown)
        {
            return true;
        }

        if (key.KeyCode == HostKey.Tab)
        {
            if (!_hasGrid)
            {
                return true;
            }

            var frame = GetFrame(_lastGrid);
            if (frame is null || frame.Actions.Count == 0)
            {
                return true;
            }

            var reverse = (key.Mods & HostKeyModifiers.Shift) != 0;
            var next = FindNextActionId(frame.Actions, _actions.State.FocusedId, reverse);
            if (next is not null)
            {
                _ = _actions.SetFocus(next);
                if (_textInput is not null)
                {
                    _textInput.SetFocus(next.Value == _textInputId, nowTicks);
                }
            }

            return true;
        }

        // Overlay steals keyboard focus while open.
        return true;
    }

    private static UIActionId? FindNextActionId(IReadOnlyList<UIAction> actions, UIActionId? current, bool reverse)
    {
        if (actions.Count == 0)
        {
            return null;
        }

        var firstEnabled = -1;
        var lastEnabled = -1;
        for (var i = 0; i < actions.Count; i++)
        {
            if (!actions[i].Enabled || actions[i].Id.IsEmpty)
            {
                continue;
            }

            if (firstEnabled < 0) firstEnabled = i;
            lastEnabled = i;
        }

        if (firstEnabled < 0)
        {
            return null;
        }

        if (current is null || current.Value.IsEmpty)
        {
            return reverse ? actions[lastEnabled].Id : actions[firstEnabled].Id;
        }

        var currentIndex = -1;
        for (var i = 0; i < actions.Count; i++)
        {
            if (actions[i].Id == current.Value)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
        {
            return reverse ? actions[lastEnabled].Id : actions[firstEnabled].Id;
        }

        if (!reverse)
        {
            for (var i = currentIndex + 1; i < actions.Count; i++)
            {
                if (actions[i].Enabled && !actions[i].Id.IsEmpty)
                {
                    return actions[i].Id;
                }
            }

            return actions[firstEnabled].Id;
        }

        for (var i = currentIndex - 1; i >= 0; i--)
        {
            if (actions[i].Enabled && !actions[i].Id.IsEmpty)
            {
                return actions[i].Id;
            }
        }

        return actions[lastEnabled].Id;
    }

    public bool HandleText(in PendingEvent.Text text)
    {
        if (_slab is null)
        {
            return false;
        }

        if (_textInput is null)
        {
            return true;
        }

        if (_actions.State.FocusedId is not { } fid || fid != _textInputId)
        {
            return true;
        }

        if (!_hasGrid)
        {
            return true;
        }

        var frame = GetFrame(_lastGrid);
        if (frame is null || frame.TextInput is null)
        {
            return true;
        }

        var input = frame.TextInput;
        var outer = input.OuterRectPx;
        var cellW = Math.Max(1, _lastGrid.CellWidthPx);
        var textStartX = outer.X + (cellW * 3);
        var innerWidth = Math.Max(0, outer.Width - (cellW * 6));
        var now = Stopwatch.GetTimestamp();
        if (_textInput.OnTextInput(text.Ch, _lastGrid, textStartX, innerWidth, now))
        {
            _frame = null;
            Version++;
        }
        return true;
    }

    public bool HandleMouse(in HostMouseEvent e, in FixedCellGrid grid)
    {
        var slab = _slab;
        if (slab is null)
        {
            return false;
        }

        var frame = GetFrame(grid);
        if (frame is null)
        {
            return slab.IsModal;
        }

        var inside = frame.OuterRectPx.Contains(e.X, e.Y);

        if (!inside)
        {
            _ = _actions.UpdateHover(Array.Empty<UIAction>(), e.X, e.Y);
        }

        if (e.Kind == HostMouseEventKind.Move)
        {
            if (inside)
            {
                if (_textInput is not null &&
                    _actions.State.FocusedId is { } fid &&
                    fid == _textInputId &&
                    (e.Buttons & HostMouseButtons.Left) != 0 &&
                    frame.TextInput is not null)
                {
                    var input = frame.TextInput;
                    var outer = input.OuterRectPx;
                    var cellW = Math.Max(1, grid.CellWidthPx);
                    var textStartX = outer.X + (cellW * 3);
                    var innerWidth = Math.Max(0, outer.Width - (cellW * 6));
                    var clickIndex = OverlayTextInputHitTest.HitTestIndex(grid, textStartX, _textInput.ScrollXPx, _textInput.Text.Length, e.X);
                    if (_textInput.OnMouseMove(clickIndex, grid, textStartX, innerWidth))
                    {
                        _frame = null;
                        Version++;
                    }
                }
                _ = _actions.UpdateHover(frame.Actions, e.X, e.Y, Math.Max(1, grid.CellHeightPx));
                return true;
            }

            return slab.IsModal;
        }

        if (e.Kind == HostMouseEventKind.Down && (e.Buttons & HostMouseButtons.Left) != 0)
        {
            if (!inside)
            {
                if (!slab.IsModal && slab.CloseOnOutsideClick)
                {
                    Close();
                    return true;
                }

                return slab.IsModal;
            }

            _ = _actions.HandleMouseDown(frame.Actions, e.X, e.Y, Math.Max(1, grid.CellHeightPx), out var focusedChangedTo);
            if (_textInput is not null && focusedChangedTo is { } fid && fid == _textInputId && frame.TextInput is not null)
            {
                var input = frame.TextInput;
                var outer = input.OuterRectPx;
                var cellW = Math.Max(1, grid.CellWidthPx);
                var textStartX = outer.X + (cellW * 3);
                var innerWidth = Math.Max(0, outer.Width - (cellW * 6));
                var shift = (e.Mods & HostKeyModifiers.Shift) != 0;
                var clickIndex = OverlayTextInputHitTest.HitTestIndex(grid, textStartX, _textInput.ScrollXPx, _textInput.Text.Length, e.X);
                if (_textInput.OnMouseDown(clickIndex, shift, grid, textStartX, innerWidth))
                {
                    _frame = null;
                    Version++;
                }
                // Text input shouldn't show "pressed" state like a button.
                _ = _actions.ClearPressed();
                _textInput.SetFocus(true, Stopwatch.GetTimestamp());
            }
            else if (_textInput is not null)
            {
                _textInput.SetFocus(false, Stopwatch.GetTimestamp());
            }
            return true;
        }

        if (e.Kind == HostMouseEventKind.Up && (e.Buttons & HostMouseButtons.Left) != 0)
        {
            if (_textInput is not null &&
                _actions.State.FocusedId is { } fid &&
                fid == _textInputId)
            {
                if (_textInput.OnMouseUp())
                {
                    _frame = null;
                    Version++;
                }
                _ = _actions.ClearPressed();
                return true;
            }

            if (_actions.State.PressedId is not null)
            {
                _ = _actions.HandleMouseUp(frame.Actions, e.X, e.Y, Math.Max(1, grid.CellHeightPx), out var activatedId);
                if (activatedId is { } id && TryFindActionById(frame.Actions, id, out var action) && action.Enabled)
                {
                    Activate(action);
                }

                return true;
            }

            return inside || slab.IsModal;
        }

        // Consume wheel when modal; otherwise let ledger scroll.
        if (e.Kind == HostMouseEventKind.Wheel)
        {
            return inside || slab.IsModal;
        }

        return inside || slab.IsModal;
    }

    private void Activate(in UIAction action)
    {
        if (action.Kind == UIActionKind.CloseOverlay)
        {
            Close();
            return;
        }

        if (action.Kind == UIActionKind.ExecuteCommand &&
            string.Equals(action.CommandText, "overlay:submit", StringComparison.Ordinal))
        {
            SubmitTextInput();
            return;
        }
    }

    private void SubmitTextInput()
    {
        if (_textInput is null)
        {
            return;
        }

        var text = _textInput.Text.Trim();
        if (_kind == OverlayKind.AiApiKey)
        {
            if (!OpenAiApiKeyStore.TrySaveToDisk(text, out var error))
            {
                _textInputError = error ?? "Failed to save API key.";
                _frame = null;
                Version++;
                return;
            }

            Close();
            return;
        }

        // Default: close on submit.
        Close();
    }

    private static int GetTextInputTextStartXPx(in FixedCellGrid grid, OverlaySlabFrame? frame)
    {
        if (frame?.TextInput is not { } input)
        {
            return 0;
        }

        var cellW = Math.Max(1, grid.CellWidthPx);
        return input.OuterRectPx.X + (cellW * 3);
    }

    private static int GetTextInputInnerWidthPx(in FixedCellGrid grid, OverlaySlabFrame? frame)
    {
        if (frame?.TextInput is not { } input)
        {
            return 0;
        }

        var cellW = Math.Max(1, grid.CellWidthPx);
        return Math.Max(0, input.OuterRectPx.Width - (cellW * 6));
    }

    private static bool TryFindActionById(IReadOnlyList<UIAction> actions, UIActionId id, out UIAction action)
    {
        action = default;
        if (actions is null || actions.Count == 0 || id.IsEmpty)
        {
            return false;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            if (a.Id == id)
            {
                action = a;
                return true;
            }
        }

        return false;
    }

    private static OverlaySlab CreateHelpControlsSlab()
    {
        var lines = new[]
        {
            "Keyboard",
            "  Esc        Close slab",
            "  Enter      Activate focused action",
            "",
            "Mouse",
            "  Hover      Highlight action",
            "  Click      Focus action",
            "  DoubleClick Activate action"
        };

        var closeLabel = "Close";
        var actions = new[]
        {
            new OverlaySlabActionSpec(
                Id: new UIActionId(1),
                Kind: UIActionKind.NoOp,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: "Back",
                CommandText: string.Empty)
            ,
            new OverlaySlabActionSpec(
                Id: new UIActionId(2),
                Kind: UIActionKind.NoOp,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: "Next",
                CommandText: string.Empty)
            ,
            new OverlaySlabActionSpec(
                Id: new UIActionId(3),
                Kind: UIActionKind.CloseOverlay,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: closeLabel,
                CommandText: string.Empty)
        };

        return new OverlaySlab(
            title: "Help / Controls",
            isModal: true,
            closeOnOutsideClick: false,
            lines: lines,
            actions: actions);
    }

    private static OverlaySlab CreateAiApiKeySlab()
    {
        var lines = new[]
        {
            ""
        };

        var actions = new[]
        {
            new OverlaySlabActionSpec(
                Id: new UIActionId(10),
                Kind: UIActionKind.TextInput,
                Area: OverlaySlabActionArea.Content,
                Align: OverlaySlabActionAlign.Left,
                RowIndex: 1,
                ColIndex: 0,
                Label: string.Empty,
                CommandText: string.Empty,
                MinWidthCols: 64),
            new OverlaySlabActionSpec(
                Id: new UIActionId(1),
                Kind: UIActionKind.ExecuteCommand,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: "Save",
                CommandText: "overlay:submit"),
            new OverlaySlabActionSpec(
                Id: new UIActionId(2),
                Kind: UIActionKind.CloseOverlay,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: "Cancel",
                CommandText: string.Empty)
        };

        return new OverlaySlab(
            title: "OpenAI API Key",
            isModal: true,
            closeOnOutsideClick: false,
            lines: lines,
            actions: actions);
    }

    private static OverlaySlab CreateInputDemoSlab()
    {
        var lines = new[]
        {
            "Type something:",
            ""
        };

        var actions = new[]
        {
            new OverlaySlabActionSpec(
                Id: new UIActionId(10),
                Kind: UIActionKind.TextInput,
                Area: OverlaySlabActionArea.Content,
                Align: OverlaySlabActionAlign.Left,
                RowIndex: 2,
                ColIndex: 0,
                Label: string.Empty,
                CommandText: string.Empty),
            new OverlaySlabActionSpec(
                Id: new UIActionId(1),
                Kind: UIActionKind.ExecuteCommand,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: "OK",
                CommandText: "overlay:submit"),
            new OverlaySlabActionSpec(
                Id: new UIActionId(2),
                Kind: UIActionKind.CloseOverlay,
                Area: OverlaySlabActionArea.Footer,
                Align: OverlaySlabActionAlign.Right,
                RowIndex: 0,
                ColIndex: 0,
                Label: "Cancel",
                CommandText: string.Empty)
        };

        return new OverlaySlab(
            title: "Input Demo",
            isModal: true,
            closeOnOutsideClick: false,
            lines: lines,
            actions: actions);
    }

    private OverlaySlabFrame BuildFrame(OverlaySlab slab, in FixedCellGrid grid)
    {
        // Overlay layout constants (cell-authored).
        const int ContentInsetCols = 2;
        const int ContentInsetRowsTop = 1; // below header separator
        const int ContentInsetRowsBottom = 1;
        const int SectionGapRows = 1;

        var cellW = Math.Max(1, grid.CellWidthPx);
        var cellH = Math.Max(1, grid.CellHeightPx);

        var contentCols = Math.Max(1, grid.Cols);
        var contentRows = Math.Max(1, grid.Rows);

        var maxLineLen = 0;
        for (var i = 0; i < slab.Lines.Count; i++)
        {
            maxLineLen = Math.Max(maxLineLen, slab.Lines[i]?.Length ?? 0);
        }

        var headerRightLen = 0;
        for (var i = 0; i < slab.Actions.Count; i++)
        {
            if (slab.Actions[i].Area == OverlaySlabActionArea.Header && slab.Actions[i].Align == OverlaySlabActionAlign.Right)
            {
                headerRightLen = Math.Max(headerRightLen, slab.Actions[i].Label?.Length ?? 0);
            }
        }

        var titleLen = slab.Title?.Length ?? 0;
        var titleRowRequired = titleLen == 0 ? headerRightLen : (titleLen + 1 + headerRightLen);

        // Chrome gutters: 16px on each side (2 cols), 16px top/bottom (1 row).
        const int chromeCols = 2;
        const int chromeRows = 1;

        // Extra breathing room inside slab interior: 1 cell padding (8px L/R, 16px T/B).
        const int padCols = 1;
        const int padRows = 1;

        var textColsMax = Math.Max(1, contentCols - (chromeCols * 2) - (padCols * 2));
        var textRowsMax = Math.Max(1, contentRows - (chromeRows * 2) - (padRows * 2));

        var minTextCols = 0;
        for (var i = 0; i < slab.Actions.Count; i++)
        {
            var a = slab.Actions[i];
            if (a.Area == OverlaySlabActionArea.Content && a.Kind == UIActionKind.TextInput && a.MinWidthCols > 0)
            {
                minTextCols = Math.Max(minTextCols, a.MinWidthCols);
            }
        }

        var textCols = Math.Clamp(Math.Max(Math.Max(maxLineLen, titleRowRequired) + (ContentInsetCols * 2), minTextCols), 20, textColsMax);
        // Content includes: 1-row title line + 1-row separator gutter + body lines.
        // Content includes: 1-row separator band + body lines + 2 footer rows (button overlaps bottom pad row).
        var sectionGapRows = slab.Lines.Count > 0 ? SectionGapRows : 0;
        var bodyRows = slab.Lines.Count + sectionGapRows;
        for (var i = 0; i < slab.Actions.Count; i++)
        {
            var a = slab.Actions[i];
            if (a.Area != OverlaySlabActionArea.Content)
            {
                continue;
            }

            // Text input is 3 rows tall; reserve 2 extra rows so we always get at least 1 blank row below it
            // before the footer buttons.
            var h = a.Kind == UIActionKind.TextInput ? 5 : 1;
            bodyRows = Math.Max(bodyRows, Math.Max(0, a.RowIndex) + h);
        }
        var gapBeforeFooterRows = ContentInsetRowsBottom; // match bottom inset for symmetry
        var textRows = Math.Clamp(
            Math.Max(ContentInsetRowsTop + bodyRows + gapBeforeFooterRows + ContentInsetRowsBottom + 2 /*footer rows*/, 6),
            6,
            textRowsMax);

        var slabCols = textCols + (padCols * 2) + (chromeCols * 2);
        var slabRows = textRows + (padRows * 2) + (chromeRows * 2);

        var slabWidthPx = slabCols * cellW;
        var slabHeightPx = slabRows * cellH;

        var contentLeftPx = grid.PaddingLeftPx;
        var contentTopPx = grid.PaddingTopPx;
        var contentWidthPx = grid.ContentWidthPx;
        var contentHeightPx = grid.ContentHeightPx;

        var startCol = Math.Max(0, (contentWidthPx - slabWidthPx) / 2 / cellW);
        var startRow = Math.Max(0, (contentHeightPx - slabHeightPx) / 2 / cellH);

        var x = contentLeftPx + (startCol * cellW);
        var y = contentTopPx + (startRow * cellH);

        var bounds = SnapToCellGrid(new PxRect(x, y, slabWidthPx, slabHeightPx), cellW, cellH);

        var actions = new List<UIAction>(slab.Actions.Count);

        var innerRect = new PxRect(
            bounds.X + (chromeCols * cellW),
            bounds.Y + (chromeRows * cellH),
            bounds.Width - (chromeCols * 2 * cellW),
            bounds.Height - (chromeRows * 2 * cellH));
        var contentRect = new PxRect(
            innerRect.X + (padCols * cellW),
            innerRect.Y + (padRows * cellH),
            innerRect.Width - (padCols * 2 * cellW),
            innerRect.Height - (padRows * 2 * cellH));

        // Header sits at the first row of the content rect; the row above is reserved as extra spacing.
        var headerX = contentRect.X;
        var headerY = contentRect.Y;
        // Footer buttons are aligned within the content rect so left/right padding stays symmetric.
        var footerY = (contentRect.Y + contentRect.Height) - (ContentInsetRowsBottom * cellH) - (cellH * 3);

        // Precompute right-aligned footer buttons from right to left with a 1-col gap.
        var footerLayoutsById = new Dictionary<UIActionId, ButtonLayoutResult>();
        {
            var gapCols = 1;
            var rightX = (contentRect.X + contentRect.Width) - (ContentInsetCols * cellW);
            for (var i = slab.Actions.Count - 1; i >= 0; i--)
            {
                var spec = slab.Actions[i];
                if (spec.Area != OverlaySlabActionArea.Footer || spec.Align != OverlaySlabActionAlign.Right)
                {
                    continue;
                }

                var layout = ButtonLayout.LayoutRightAligned3Row(grid, rightX, footerY, spec.Label ?? string.Empty);
                footerLayoutsById[spec.Id] = layout;
                rightX = layout.OuterRectPx.X - (gapCols * cellW);
            }
        }

        for (var i = 0; i < slab.Actions.Count; i++)
        {
            var spec = slab.Actions[i];
            var label = spec.Label ?? string.Empty;
            var labelLen = label.Length;
            var aw = Math.Max(1, labelLen * cellW);
            var ah = cellH;

            int ax;
            int ay;

            if (spec.Area == OverlaySlabActionArea.Header)
            {
                ay = headerY + (Math.Max(0, spec.RowIndex) * cellH);
                if (spec.Align == OverlaySlabActionAlign.Right)
                {
                    ax = headerX + contentRect.Width - aw - (Math.Max(0, spec.ColIndex) * cellW);
                }
                else
                {
                    ax = headerX + (Math.Max(0, spec.ColIndex) * cellW);
                }
            }
            else if (spec.Area == OverlaySlabActionArea.Footer)
            {
                if (footerLayoutsById.TryGetValue(spec.Id, out var layout))
                {
                    ax = layout.OuterRectPx.X;
                    ay = layout.OuterRectPx.Y;
                    aw = layout.OuterRectPx.Width;
                    ah = layout.OuterRectPx.Height;
                }
                else
                {
                    ay = footerY;
                    ax = headerX + contentRect.Width - aw;
                }
            }
            else
            {
                // Content actions share the same body-top baseline as slab lines.
                var bodyTopY = contentRect.Y + ((1 + ContentInsetRowsTop) * cellH);
                ay = bodyTopY + (Math.Max(0, spec.RowIndex) * cellH);

                ax = contentRect.X + (ContentInsetCols * cellW) + (Math.Max(0, spec.ColIndex) * cellW);
                if (spec.Kind == UIActionKind.TextInput)
                {
                    // Wide input: full content width minus symmetric insets.
                    aw = Math.Max(cellW * 8, contentRect.Width - (ContentInsetCols * 2 * cellW));
                    ah = cellH * 3;
                    ax = contentRect.X + (ContentInsetCols * cellW);
                }
                else
                {
                    aw = Math.Max(1, labelLen * cellW);
                    ah = cellH;
                }
            }

            var rect = new PxRect(ax, ay, aw, ah);
            actions.Add(new UIAction(
                Id: spec.Id,
                RectPx: rect,
                Label: label,
                CommandText: spec.CommandText ?? string.Empty,
                Enabled: spec.Enabled,
                Kind: spec.Kind,
                BlockId: default,
                CharStart: 0,
                CharLength: 0));
        }

        OverlayTextInputFrame? textInput = null;
        if (_textInput is not null)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                if (actions[i].Id == _textInputId && actions[i].Kind == UIActionKind.TextInput)
                {
                    textInput = new OverlayTextInputFrame(
                        id: _textInputId,
                        outerRectPx: actions[i].RectPx,
                        text: _textInput.Text,
                        caretIndex: _textInput.CaretIndex,
                        selectionAnchorIndex: _textInput.SelectionAnchorIndex,
                        scrollXPx: _textInput.ScrollXPx,
                        caretAlpha: (_textInput.SelectionAnchorIndex is { } a && a != _textInput.CaretIndex) ? (byte)0 : _textInput.CaretAlpha);
                    break;
                }
            }
        }

        var lines = slab.Lines;
        if (_kind == OverlayKind.AiApiKey && !string.IsNullOrEmpty(_textInputError))
        {
            // Keep errors in the reserved line above the input so we don't overlap the text input rows.
            var updated = new List<string>(slab.Lines.Count);
            for (var i = 0; i < slab.Lines.Count; i++)
            {
                updated.Add(slab.Lines[i]);
            }

            if (updated.Count == 0)
            {
                updated.Add("Error: " + _textInputError);
            }
            else
            {
                updated[^1] = "Error: " + _textInputError;
            }

            lines = updated;
        }

        return new OverlaySlabFrame(bounds, contentRect, slab.IsModal, slab.Title ?? string.Empty, lines, actions, textInput);
    }

    private static PxRect SnapToCellGrid(PxRect rect, int cellW, int cellH)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return rect;
        }

        cellW = Math.Max(1, cellW);
        cellH = Math.Max(1, cellH);

        var snappedW = rect.Width >= cellW ? rect.Width - (rect.Width % cellW) : rect.Width;
        var snappedH = rect.Height >= cellH ? rect.Height - (rect.Height % cellH) : rect.Height;
        return new PxRect(rect.X, rect.Y, snappedW, snappedH);
    }
}
