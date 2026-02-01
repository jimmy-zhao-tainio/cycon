using System;
using Cycon.Host.Hosting;
using Cycon.Host.Input;
using Cycon.Layout.Metrics;

namespace Cycon.Host.Overlays;

internal sealed class OverlayTextInput
{
    private readonly PromptCaretController _caret = new();
    private bool _dragSelecting;

    public string Text { get; private set; } = string.Empty;
    public int CaretIndex { get; private set; }
    public int? SelectionAnchorIndex { get; private set; }
    public int ScrollXPx { get; private set; }
    public bool HasFocus { get; private set; }
    public byte CaretAlpha { get; private set; }
    public long NextDeadlineTicks => _caret.NextDeadlineTicks;

    public void SetText(string text)
    {
        Text = text ?? string.Empty;
        CaretIndex = Math.Clamp(CaretIndex, 0, Text.Length);
        if (SelectionAnchorIndex is { } a)
        {
            SelectionAnchorIndex = Math.Clamp(a, 0, Text.Length);
        }
    }

    public void SetFocus(bool hasFocus, long nowTicks)
    {
        if (HasFocus == hasFocus)
        {
            return;
        }

        HasFocus = hasFocus;
        _caret.SetPromptFocused(hasFocus, nowTicks);
        _caret.SetSuppressed(!hasFocus, nowTicks);
        _caret.Update(nowTicks);
        CaretAlpha = _caret.SampleAlpha(nowTicks);
        if (!hasFocus)
        {
            _dragSelecting = false;
            SelectionAnchorIndex = null;
        }
    }

    public bool Tick(long nowTicks)
    {
        var before = CaretAlpha;
        _caret.SetSuppressed(!HasFocus, nowTicks);
        _caret.SetPromptFocused(HasFocus, nowTicks);
        _caret.Update(nowTicks);
        CaretAlpha = _caret.SampleAlpha(nowTicks);
        return before != CaretAlpha;
    }

    public bool OnTextInput(char ch, in FixedCellGrid grid, int textStartXPx, int innerWidthPx, long nowTicks)
    {
        if (!HasFocus || char.IsControl(ch))
        {
            return false;
        }

        ReplaceSelectionIfAny(out var start, out var end);
        var caret = start;

        Text = Text.Insert(caret, ch.ToString());
        CaretIndex = caret + 1;
        SelectionAnchorIndex = null;

        _caret.OnTyped(nowTicks);
        _caret.Update(nowTicks);
        CaretAlpha = _caret.SampleAlpha(nowTicks);
        EnsureCaretVisible(grid, textStartXPx, innerWidthPx);
        return true;
    }

    public bool OnKeyDown(
        in PendingEvent.Key key,
        Func<string?> getClipboardText,
        Action<string>? setClipboardText,
        in FixedCellGrid grid,
        int textStartXPx,
        int innerWidthPx,
        long nowTicks,
        out bool submitted)
    {
        submitted = false;
        if (!HasFocus)
        {
            return false;
        }

        if (!key.IsDown)
        {
            return false;
        }

        // Allow overlay-level navigation/close keys to be handled by the overlay manager.
        // Everything else should immediately force the caret into "typed" (solid) mode.
        if (key.KeyCode is HostKey.Tab or HostKey.Escape or HostKey.Up or HostKey.Down or HostKey.PageUp or HostKey.PageDown)
        {
            return false;
        }

        var mods = key.Mods;
        var ctrl = (mods & HostKeyModifiers.Control) != 0;
        var shift = (mods & HostKeyModifiers.Shift) != 0;

        _caret.OnTyped(nowTicks);
        _caret.Update(nowTicks);
        CaretAlpha = _caret.SampleAlpha(nowTicks);

        if (ctrl && key.KeyCode == HostKey.A)
        {
            SelectionAnchorIndex = 0;
            CaretIndex = Text.Length;
            EnsureCaretVisible(grid, textStartXPx, innerWidthPx);
            return true;
        }

        if (ctrl && key.KeyCode == HostKey.C)
        {
            if (TryGetSelection(out var s, out var e) && e > s)
            {
                setClipboardText?.Invoke(Text.Substring(s, e - s));
            }

            return true;
        }

        if (ctrl && key.KeyCode == HostKey.X)
        {
            if (TryGetSelection(out var s, out var e) && e > s)
            {
                setClipboardText?.Invoke(Text.Substring(s, e - s));
                Text = Text.Remove(s, e - s);
                CaretIndex = s;
                SelectionAnchorIndex = null;
                _caret.OnTyped(nowTicks);
                EnsureCaretVisible(grid, textStartXPx, innerWidthPx);
            }

            return true;
        }

        if (ctrl && key.KeyCode == HostKey.V)
        {
            var paste = getClipboardText?.Invoke() ?? string.Empty;
            paste = paste.Replace("\r", string.Empty, StringComparison.Ordinal)
                         .Replace("\n", string.Empty, StringComparison.Ordinal);
            if (paste.Length == 0)
            {
                return true;
            }

            ReplaceSelectionIfAny(out var s, out var e);
            Text = Text.Remove(s, e - s).Insert(s, paste);
            CaretIndex = s + paste.Length;
            SelectionAnchorIndex = null;
            _caret.OnTyped(nowTicks);
            EnsureCaretVisible(grid, textStartXPx, innerWidthPx);
            return true;
        }

        if (key.KeyCode == HostKey.Enter)
        {
            submitted = true;
            return true;
        }

        switch (key.KeyCode)
        {
            case HostKey.Backspace:
                if (TryGetSelection(out var s, out var e) && e > s)
                {
                    Text = Text.Remove(s, e - s);
                    CaretIndex = s;
                    SelectionAnchorIndex = null;
                }
                else if (CaretIndex > 0 && Text.Length > 0)
                {
                    var caret = Math.Clamp(CaretIndex, 0, Text.Length);
                    Text = Text.Remove(caret - 1, 1);
                    CaretIndex = caret - 1;
                }
                _caret.OnTyped(nowTicks);
                EnsureCaretVisible(grid, textStartXPx, innerWidthPx);
                return true;

            case HostKey.Delete:
                if (TryGetSelection(out s, out e) && e > s)
                {
                    Text = Text.Remove(s, e - s);
                    CaretIndex = s;
                    SelectionAnchorIndex = null;
                }
                else if (CaretIndex < Text.Length)
                {
                    var caret = Math.Clamp(CaretIndex, 0, Text.Length);
                    Text = Text.Remove(caret, 1);
                    CaretIndex = caret;
                }
                _caret.OnTyped(nowTicks);
                EnsureCaretVisible(grid, textStartXPx, innerWidthPx);
                return true;

            case HostKey.Left:
            case HostKey.Right:
            case HostKey.Home:
            case HostKey.End:
                MoveCaretForNav(key.KeyCode, ctrl, shift);
                EnsureCaretVisible(grid, textStartXPx, innerWidthPx);
                return true;

            default:
                // Swallow any other key downs (letters etc.) so the prompt doesn't see them while the input is focused.
                return true;
        }
    }

    public bool OnMouseDown(int clickIndex, bool shift, in FixedCellGrid grid, int textStartXPx, int innerWidthPx)
    {
        if (!HasFocus)
        {
            return false;
        }

        clickIndex = Math.Clamp(clickIndex, 0, Text.Length);

        if (shift)
        {
            var anchor = SelectionAnchorIndex ?? CaretIndex;
            SelectionAnchorIndex = anchor;
            CaretIndex = clickIndex;
        }
        else
        {
            SelectionAnchorIndex = clickIndex;
            CaretIndex = clickIndex;
        }

        _dragSelecting = true;
        EnsureCaretVisible(grid, textStartXPx, innerWidthPx);
        return true;
    }

    public bool OnMouseMove(int clickIndex, in FixedCellGrid grid, int textStartXPx, int innerWidthPx)
    {
        if (!HasFocus || !_dragSelecting || SelectionAnchorIndex is null)
        {
            return false;
        }

        clickIndex = Math.Clamp(clickIndex, 0, Text.Length);
        CaretIndex = clickIndex;
        EnsureCaretVisible(grid, textStartXPx, innerWidthPx);
        return true;
    }

    public bool OnMouseUp()
    {
        if (!_dragSelecting)
        {
            return false;
        }

        _dragSelecting = false;
        if (SelectionAnchorIndex == CaretIndex)
        {
            SelectionAnchorIndex = null;
        }

        return true;
    }

    public bool TryGetSelection(out int start, out int end)
    {
        start = 0;
        end = 0;
        if (SelectionAnchorIndex is not { } anchor)
        {
            return false;
        }

        start = Math.Clamp(Math.Min(anchor, CaretIndex), 0, Text.Length);
        end = Math.Clamp(Math.Max(anchor, CaretIndex), 0, Text.Length);
        return true;
    }

    private void ReplaceSelectionIfAny(out int start, out int end)
    {
        start = Math.Clamp(CaretIndex, 0, Text.Length);
        end = start;
        if (!TryGetSelection(out var s, out var e) || e <= s)
        {
            return;
        }

        Text = Text.Remove(s, e - s);
        CaretIndex = s;
        start = s;
        end = s;
    }

    private void MoveCaretForNav(HostKey key, bool ctrl, bool shift)
    {
        var caret = Math.Clamp(CaretIndex, 0, Text.Length);
        var next = caret;
        switch (key)
        {
            case HostKey.Home:
                next = 0;
                break;
            case HostKey.End:
                next = Text.Length;
                break;
            case HostKey.Left:
                next = ctrl ? FindPrevWordBoundary(Text, caret) : Math.Max(0, caret - 1);
                break;
            case HostKey.Right:
                next = ctrl ? FindNextWordBoundary(Text, caret) : Math.Min(Text.Length, caret + 1);
                break;
        }

        if (shift)
        {
            SelectionAnchorIndex ??= caret;
        }
        else
        {
            SelectionAnchorIndex = null;
        }

        CaretIndex = next;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int FindPrevWordBoundary(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        if (caret == 0)
        {
            return 0;
        }

        var i = caret - 1;
        while (i > 0 && !IsWordChar(text[i]))
        {
            i--;
        }

        while (i > 0 && IsWordChar(text[i - 1]))
        {
            i--;
        }

        return i;
    }

    private static int FindNextWordBoundary(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        if (caret >= text.Length)
        {
            return text.Length;
        }

        var i = caret;
        while (i < text.Length && !IsWordChar(text[i]))
        {
            i++;
        }

        while (i < text.Length && IsWordChar(text[i]))
        {
            i++;
        }

        return i;
    }

    private void EnsureCaretVisible(in FixedCellGrid grid, int textStartXPx, int innerWidthPx)
    {
        var cellW = Math.Max(1, grid.CellWidthPx);
        var margin = cellW;
        var caretX = OverlayTextInputHitTest.MeasureTextXPx(grid, Math.Clamp(CaretIndex, 0, Text.Length));

        var viewW = Math.Max(0, innerWidthPx);
        if (viewW <= 0)
        {
            ScrollXPx = 0;
            return;
        }

        var minVisible = ScrollXPx + margin;
        var maxVisible = ScrollXPx + Math.Max(margin, viewW - margin);

        if (caretX < minVisible)
        {
            ScrollXPx = Math.Max(0, caretX - margin);
        }
        else if (caretX > maxVisible)
        {
            ScrollXPx = Math.Max(0, caretX - Math.Max(margin, viewW - margin));
        }

        // Clamp scroll to text width.
        var maxScroll = Math.Max(0, OverlayTextInputHitTest.MeasureTextXPx(grid, Text.Length) - Math.Max(0, viewW - cellW));
        ScrollXPx = Math.Clamp(ScrollXPx, 0, maxScroll);
    }
}
