using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Cycon.Core.Selection;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Input;
using Cycon.Layout;
using Cycon.Layout.HitTesting;

namespace Cycon.Host.Interaction;

public sealed class InteractionReducer
{
    private readonly InteractionState _state = new();
    private readonly HitTester _hitTester = new();

    public InteractionSnapshot Snapshot =>
        new(_state.Focused, _state.MouseCaptured, _state.IsSelecting, _state.Selection, _state.CurrentMods, _state.LastPromptId);

    public void Initialize(Transcript transcript)
    {
        _state.LastPromptId = FindLastPromptId(transcript);
        _state.Focused = _state.LastPromptId ?? FindLastBlockId(transcript);
        _state.MouseCaptured = null;
        _state.IsSelecting = false;
        _state.Selection = null;
        _state.SelectionCaret = null;
    }

    public IReadOnlyList<HostAction> Handle(InputEvent e, LayoutFrame frame, Transcript transcript)
    {
        _state.LastPromptId = FindLastPromptId(transcript);
        _state.CurrentMods = e switch
        {
            InputEvent.KeyDown kd => kd.Mods,
            InputEvent.KeyUp ku => ku.Mods,
            InputEvent.MouseDown md => md.Mods,
            InputEvent.MouseMove mm => mm.Mods,
            InputEvent.MouseUp mu => mu.Mods,
            InputEvent.MouseWheel mw => mw.Mods,
            _ => _state.CurrentMods
        };

        HealStateAfterTranscriptChange(transcript);

        var actions = e switch
        {
            InputEvent.Text text => HandleText(text, transcript),
            InputEvent.KeyDown keyDown => HandleKeyDown(keyDown, frame, transcript),
            InputEvent.KeyUp => Array.Empty<HostAction>(),
            InputEvent.MouseDown mouseDown => HandleMouseDown(mouseDown, frame, transcript),
            InputEvent.MouseMove mouseMove => HandleMouseMove(mouseMove, frame, transcript),
            InputEvent.MouseUp mouseUp => HandleMouseUp(mouseUp),
            InputEvent.MouseWheel => Array.Empty<HostAction>(),
            _ => Array.Empty<HostAction>()
        };

        ValidateState(transcript);
        return actions;
    }

    public bool TryGetSelectedText(Transcript transcript, out string text)
    {
        text = string.Empty;
        if (_state.Selection is not { } range || range.Anchor == range.Caret)
        {
            return false;
        }

        if (!TryNormalizeRange(transcript, range, out var start, out var end, out var startBlockIndex, out var endBlockIndex))
        {
            return false;
        }

        var builder = new StringBuilder();
        var blocks = transcript.Blocks;
        for (var blockIndex = startBlockIndex; blockIndex <= endBlockIndex; blockIndex++)
        {
            if (blocks[blockIndex] is not ITextSelectable selectable || !selectable.CanSelect)
            {
                continue;
            }

            var blockStart = 0;
            var blockEnd = selectable.TextLength;

            if (blockIndex == startBlockIndex)
            {
                blockStart = Math.Clamp(start.Index, 0, selectable.TextLength);
            }

            if (blockIndex == endBlockIndex)
            {
                blockEnd = Math.Clamp(end.Index, 0, selectable.TextLength);
            }

            var length = Math.Max(0, blockEnd - blockStart);
            if (length > 0)
            {
                builder.Append(selectable.ExportText(blockStart, length));
            }

            if (blockIndex != endBlockIndex)
            {
                builder.Append('\n');
            }
        }

        text = builder.ToString();
        return text.Length > 0;
    }

    private IReadOnlyList<HostAction> HandleText(InputEvent.Text e, Transcript transcript)
    {
        if (char.IsControl(e.Ch))
        {
            return Array.Empty<HostAction>();
        }

        var actions = new List<HostAction>();
        var promptId = _state.LastPromptId;
        if (promptId is null)
        {
            return actions;
        }

        actions.AddRange(CancelSelectionAndCapture(actions));
        _state.Focused = promptId;
        actions.Add(new HostAction.Focus(promptId.Value));
        actions.Add(new HostAction.InsertText(promptId.Value, e.Ch.ToString()));
        actions.Add(new HostAction.RequestRebuild());
        return actions;
    }

    private IReadOnlyList<HostAction> HandleKeyDown(InputEvent.KeyDown e, LayoutFrame frame, Transcript transcript)
    {
        var actions = new List<HostAction>();

        if (e.Key == HostKey.Unknown)
        {
            return actions;
        }

        var lastPromptId = _state.LastPromptId;

        if (e.Key == HostKey.Escape)
        {
            _state.MouseCaptured = null;
            _state.IsSelecting = false;
            actions.Add(new HostAction.SetMouseCapture(null));

            if (_state.Selection is not null)
            {
                _state.Selection = null;
                actions.Add(new HostAction.ClearSelection());
            }

            _state.SelectionCaret = null;

            if (lastPromptId is not null)
            {
                _state.Focused = lastPromptId;
                actions.Add(new HostAction.Focus(lastPromptId.Value));
            }

            actions.Add(new HostAction.RequestRebuild());
            return actions;
        }

        var ctrl = (e.Mods & HostKeyModifiers.Control) != 0;
        var shift = (e.Mods & HostKeyModifiers.Shift) != 0;

        if (ctrl && e.Key == HostKey.A)
        {
            if (TryCreateSelectAllRange(transcript, out var range))
            {
                _state.MouseCaptured = null;
                _state.IsSelecting = false;
                actions.Add(new HostAction.SetMouseCapture(null));

                _state.Selection = range;
                _state.SelectionCaret = range.Caret;
                actions.Add(new HostAction.RequestRebuild());
            }

            return actions;
        }

        if (shift && e.Key is HostKey.Left or HostKey.Right or HostKey.Up or HostKey.Down)
        {
            if (TryHandleKeyboardSelectionExtend(e.Key, frame, transcript))
            {
                actions.Add(new HostAction.RequestRebuild());
            }

            return actions;
        }

        if (ctrl && e.Key == HostKey.C)
        {
            if (_state.Selection is not null)
            {
                actions.Add(new HostAction.CopySelectionToClipboard());
            }
            else
            {
                actions.Add(new HostAction.StopFocusedBlockWithLevel(StopLevel.Kill));
                actions.Add(new HostAction.RequestRebuild());
            }

            return actions;
        }

        if (ctrl && e.Key == HostKey.V)
        {
            if (lastPromptId is null)
            {
                return actions;
            }

            actions.AddRange(CancelSelectionAndCapture(actions));
            _state.Focused = lastPromptId;
            actions.Add(new HostAction.Focus(lastPromptId.Value));
            actions.Add(new HostAction.PasteFromClipboardIntoLastPrompt());
            actions.Add(new HostAction.RequestRebuild());
            return actions;
        }

        if (lastPromptId is null)
        {
            return actions;
        }

        if (e.Key is HostKey.Backspace or HostKey.Enter or HostKey.Tab or HostKey.Left or HostKey.Right or HostKey.Up or HostKey.Down)
        {
            actions.AddRange(CancelSelectionAndCapture(actions));
            _state.Focused = lastPromptId;
            actions.Add(new HostAction.Focus(lastPromptId.Value));
        }
        else if (_state.Focused is null || !IsEditablePrompt(transcript, _state.Focused.Value))
        {
            _state.Focused = lastPromptId;
            actions.Add(new HostAction.Focus(lastPromptId.Value));
        }

        var promptId = lastPromptId.Value;
        switch (e.Key)
        {
            case HostKey.Backspace:
                actions.Add(new HostAction.Backspace(promptId));
                actions.Add(new HostAction.RequestRebuild());
                break;
            case HostKey.Tab:
                if (TryGetPrompt(transcript, promptId, out var tabPrompt) && tabPrompt.Owner is null)
                {
                    var reverse = (e.Mods & HostKeyModifiers.Shift) != 0;
                    actions.Add(new HostAction.Autocomplete(promptId, reverse ? -1 : 1));
                    actions.Add(new HostAction.RequestRebuild());
                }
                break;
            case HostKey.Left:
                actions.Add(new HostAction.MoveCaret(promptId, -1));
                actions.Add(new HostAction.RequestRebuild());
                break;
            case HostKey.Right:
                actions.Add(new HostAction.MoveCaret(promptId, 1));
                actions.Add(new HostAction.RequestRebuild());
                break;
            case HostKey.Up:
                actions.Add(new HostAction.NavigateHistory(promptId, -1));
                actions.Add(new HostAction.RequestRebuild());
                break;
            case HostKey.Down:
                actions.Add(new HostAction.NavigateHistory(promptId, 1));
                actions.Add(new HostAction.RequestRebuild());
                break;
            case HostKey.Enter:
                actions.Add(new HostAction.SubmitPrompt(promptId));
                actions.Add(new HostAction.RequestRebuild());
                break;
        }

        return actions;
    }

    private IReadOnlyList<HostAction> HandleMouseDown(InputEvent.MouseDown e, LayoutFrame frame, Transcript transcript)
    {
        var actions = new List<HostAction>();
        if (_state.MouseCaptured is not null)
        {
            _state.MouseCaptured = null;
            _state.IsSelecting = false;
            actions.Add(new HostAction.SetMouseCapture(null));
        }

        if (e.Button != MouseButton.Left)
        {
            return Array.Empty<HostAction>();
        }

        var pos = _hitTester.HitTest(frame.HitTestMap, e.X, e.Y);
        if (pos is null)
        {
            if (_state.Selection is not null)
            {
                _state.Selection = null;
                actions.Add(new HostAction.ClearSelection());
                actions.Add(new HostAction.RequestRebuild());
            }

            return actions;
        }

        var normalizedCharIndex = NormalizePromptPrefix(transcript, pos.Value.BlockId, pos.Value.CharIndex);

        if ((e.Mods & HostKeyModifiers.Shift) == 0 &&
            frame.HitTestMap.TryGetActionAt(e.X, e.Y, out var commandText) &&
            FindLastPromptId(transcript) is { } lastPromptId &&
            TryGetPrompt(transcript, lastPromptId, out var promptForInsert) &&
            promptForInsert.Owner is null)
        {
            _state.Focused = lastPromptId;
            _state.Selection = null;
            _state.SelectionCaret = null;
            _state.IsSelecting = false;
            _state.MouseCaptured = null;

            actions.Add(new HostAction.Focus(lastPromptId));
            actions.Add(new HostAction.SetPromptInput(lastPromptId, commandText, commandText.Length));
            actions.Add(new HostAction.RequestRebuild());
            return actions;
        }

        _state.Focused = pos.Value.BlockId;
        actions.Add(new HostAction.Focus(pos.Value.BlockId));

        if (TryGetPrompt(transcript, pos.Value.BlockId, out var prompt))
        {
            var inputIndex = Math.Clamp(normalizedCharIndex - prompt.PromptPrefixLength, 0, prompt.Input.Length);
            actions.Add(new HostAction.SetCaret(pos.Value.BlockId, inputIndex));
        }

        var caretPos = new SelectionPosition(pos.Value.BlockId, normalizedCharIndex);

        var shift = (e.Mods & HostKeyModifiers.Shift) != 0;
        if (shift)
        {
            var anchor = _state.Selection?.Anchor ?? _state.SelectionCaret ?? GetPromptSelectionCaret(transcript) ?? caretPos;
            _state.Selection = new SelectionRange(anchor, caretPos);
            _state.SelectionCaret = caretPos;
            actions.Add(new HostAction.RequestRebuild());
            return actions;
        }

        _state.SelectionCaret = caretPos;
        _state.IsSelecting = true;
        _state.MouseCaptured = pos.Value.BlockId;
        actions.Add(new HostAction.SetMouseCapture(pos.Value.BlockId));

        _state.Selection = new SelectionRange(caretPos, caretPos);
        actions.Add(new HostAction.RequestRebuild());
        return actions;
    }

    private IReadOnlyList<HostAction> HandleMouseMove(InputEvent.MouseMove e, LayoutFrame frame, Transcript transcript)
    {
        if (_state.IsSelecting && (e.Buttons & HostMouseButtons.Left) == 0)
        {
            _state.IsSelecting = false;
            _state.MouseCaptured = null;
            return new HostAction[] { new HostAction.SetMouseCapture(null), new HostAction.RequestRebuild() };
        }

        if (!_state.IsSelecting || _state.MouseCaptured is null)
        {
            return Array.Empty<HostAction>();
        }

        var pos = _hitTester.HitTest(frame.HitTestMap, e.X, e.Y);
        if (pos is null || _state.Selection is not { } range)
        {
            return Array.Empty<HostAction>();
        }

        var normalizedCharIndex = NormalizePromptPrefix(transcript, pos.Value.BlockId, pos.Value.CharIndex);
        var caret = new SelectionPosition(pos.Value.BlockId, normalizedCharIndex);
        var updated = new SelectionRange(range.Anchor, caret);
        if (updated == range)
        {
            return Array.Empty<HostAction>();
        }

        _state.Selection = updated;
        return new HostAction[] { new HostAction.RequestRebuild() };
    }

    private IReadOnlyList<HostAction> HandleMouseUp(InputEvent.MouseUp e)
    {
        if (e.Button != MouseButton.Left)
        {
            return Array.Empty<HostAction>();
        }

        if (!_state.IsSelecting && _state.MouseCaptured is null)
        {
            return Array.Empty<HostAction>();
        }

        _state.IsSelecting = false;
        _state.MouseCaptured = null;

        var actions = new List<HostAction> { new HostAction.SetMouseCapture(null) };

        if (_state.Selection is { } range && range.Anchor == range.Caret)
        {
            _state.Selection = null;
            actions.Add(new HostAction.ClearSelection());
        }

        actions.Add(new HostAction.RequestRebuild());
        return actions;
    }

    private IEnumerable<HostAction> CancelSelectionAndCapture(List<HostAction> actions)
    {
        if (_state.MouseCaptured is not null)
        {
            _state.MouseCaptured = null;
            _state.IsSelecting = false;
            actions.Add(new HostAction.SetMouseCapture(null));
        }

        if (_state.Selection is not null)
        {
            _state.Selection = null;
            actions.Add(new HostAction.ClearSelection());
        }

        return Array.Empty<HostAction>();
    }

    private static BlockId? FindLastPromptId(Transcript transcript)
    {
        for (var i = transcript.Blocks.Count - 1; i >= 0; i--)
        {
            if (transcript.Blocks[i] is PromptBlock prompt)
            {
                return prompt.Id;
            }
        }

        return null;
    }

    private static bool IsEditablePrompt(Transcript transcript, BlockId id)
    {
        return TryGetPrompt(transcript, id, out _);
    }

    private static bool TryGetPrompt(Transcript transcript, BlockId id, out PromptBlock prompt)
    {
        foreach (var block in transcript.Blocks)
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

    private static bool TryGetBlock(Transcript transcript, BlockId id, out IBlock block)
    {
        foreach (var candidate in transcript.Blocks)
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

    private static int NormalizePromptPrefix(Transcript transcript, BlockId blockId, int charIndex)
    {
        if (!TryGetPrompt(transcript, blockId, out var prompt))
        {
            return charIndex;
        }

        return Math.Max(charIndex, prompt.PromptPrefixLength);
    }

    private SelectionPosition? GetPromptSelectionCaret(Transcript transcript)
    {
        if (_state.LastPromptId is not { } promptId ||
            !TryGetPrompt(transcript, promptId, out var prompt))
        {
            return null;
        }

        var inputCaret = Math.Clamp(prompt.CaretIndex, 0, prompt.Input.Length);
        var charIndex = prompt.PromptPrefixLength + inputCaret;
        return new SelectionPosition(promptId, charIndex);
    }

    private bool TryHandleKeyboardSelectionExtend(HostKey key, LayoutFrame frame, Transcript transcript)
    {
        var range = _state.Selection;
        SelectionPosition anchor;
        SelectionPosition caret;

        if (range is { } existing)
        {
            anchor = existing.Anchor;
            caret = existing.Caret;
        }
        else
        {
            var startCaret = _state.SelectionCaret ?? GetPromptSelectionCaret(transcript);
            if (startCaret is null)
            {
                return false;
            }

            anchor = startCaret.Value;
            caret = startCaret.Value;
        }

        if (!TryMoveSelectionCaret(key, frame, transcript, caret, out var nextCaret))
        {
            return false;
        }

        var updated = new SelectionRange(anchor, nextCaret);
        if (updated == range)
        {
            return false;
        }

        _state.Selection = updated;
        _state.SelectionCaret = nextCaret;
        return true;
    }

    private static bool TryCreateSelectAllRange(Transcript transcript, out SelectionRange range)
    {
        range = default;

        var blocks = transcript.Blocks;
        if (blocks.Count == 0)
        {
            return false;
        }

        var foundStart = false;
        var start = default(SelectionPosition);
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not ITextSelectable selectable || !selectable.CanSelect)
            {
                continue;
            }

            var selectableStart = blocks[i] is PromptBlock prompt ? prompt.PromptPrefixLength : 0;
            if (selectable.TextLength <= selectableStart)
            {
                continue;
            }

            start = new SelectionPosition(blocks[i].Id, selectableStart);
            foundStart = true;
            break;
        }

        if (!foundStart)
        {
            return false;
        }

        var foundEnd = false;
        var end = default(SelectionPosition);
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            if (blocks[i] is not ITextSelectable selectable || !selectable.CanSelect)
            {
                continue;
            }

            var selectableStart = blocks[i] is PromptBlock prompt ? prompt.PromptPrefixLength : 0;
            if (selectable.TextLength <= selectableStart)
            {
                continue;
            }

            end = new SelectionPosition(blocks[i].Id, selectable.TextLength);
            foundEnd = true;
            break;
        }

        if (!foundEnd)
        {
            return false;
        }

        range = new SelectionRange(start, end);
        return range.Anchor != range.Caret;
    }

    private static bool TryMoveSelectionCaret(
        HostKey key,
        LayoutFrame frame,
        Transcript transcript,
        SelectionPosition caret,
        out SelectionPosition moved)
    {
        moved = caret;

        switch (key)
        {
            case HostKey.Left:
                return TryMoveSelectionCaretHorizontal(transcript, caret, delta: -1, out moved);
            case HostKey.Right:
                return TryMoveSelectionCaretHorizontal(transcript, caret, delta: 1, out moved);
            case HostKey.Up:
                return TryMoveSelectionCaretVertical(frame, transcript, caret, deltaRows: -1, out moved);
            case HostKey.Down:
                return TryMoveSelectionCaretVertical(frame, transcript, caret, deltaRows: 1, out moved);
            default:
                return false;
        }
    }

    private static bool TryMoveSelectionCaretHorizontal(
        Transcript transcript,
        SelectionPosition caret,
        int delta,
        out SelectionPosition moved)
    {
        moved = caret;

        if (!TryFindBlockIndex(transcript, caret.BlockId, out var blockIndex))
        {
            return false;
        }

        var blocks = transcript.Blocks;
        var block = blocks[blockIndex];
        if (block is not ITextSelectable selectable || !selectable.CanSelect)
        {
            return TryMoveToAdjacentSelectableBlock(transcript, blockIndex, delta, out moved);
        }

        var selectableStart = block is PromptBlock prompt ? prompt.PromptPrefixLength : 0;
        var selectableEnd = selectable.TextLength;
        var clampedIndex = Math.Clamp(caret.Index, selectableStart, selectableEnd);

        if (delta < 0)
        {
            if (clampedIndex > selectableStart)
            {
                moved = new SelectionPosition(caret.BlockId, clampedIndex - 1);
                return moved != caret;
            }

            return TryMoveToAdjacentSelectableBlock(transcript, blockIndex, delta, out moved);
        }

        if (clampedIndex < selectableEnd)
        {
            moved = new SelectionPosition(caret.BlockId, clampedIndex + 1);
            return moved != caret;
        }

        return TryMoveToAdjacentSelectableBlock(transcript, blockIndex, delta, out moved);
    }

    private static bool TryMoveToAdjacentSelectableBlock(
        Transcript transcript,
        int fromBlockIndex,
        int delta,
        out SelectionPosition moved)
    {
        moved = default;

        var blocks = transcript.Blocks;
        if (blocks.Count == 0)
        {
            return false;
        }

        var i = fromBlockIndex + Math.Sign(delta);
        while (i >= 0 && i < blocks.Count)
        {
            var block = blocks[i];
            if (block is ITextSelectable selectable && selectable.CanSelect)
            {
                var selectableStart = block is PromptBlock prompt ? prompt.PromptPrefixLength : 0;
                var selectableEnd = selectable.TextLength;
                if (selectableEnd > selectableStart)
                {
                    moved = delta < 0
                        ? new SelectionPosition(block.Id, selectableEnd)
                        : new SelectionPosition(block.Id, selectableStart);
                    return true;
                }
            }

            i += Math.Sign(delta);
        }

        return false;
    }

    private static bool TryMoveSelectionCaretVertical(
        LayoutFrame frame,
        Transcript transcript,
        SelectionPosition caret,
        int deltaRows,
        out SelectionPosition moved)
    {
        moved = caret;

        if (!TryFindLineForCaret(frame.HitTestMap, caret, out var line, out var col))
        {
            return false;
        }

        if (frame.HitTestMap.Lines.Count == 0)
        {
            return false;
        }

        var lastRow = frame.HitTestMap.Lines[^1].RowIndex;
        var step = Math.Sign(deltaRows);

        for (var row = line.RowIndex + step; row >= 0 && row <= lastRow; row += step)
        {
            if (!frame.HitTestMap.TryGetLine(row, out var target))
            {
                continue;
            }

            if ((uint)target.BlockIndex >= (uint)transcript.Blocks.Count)
            {
                continue;
            }

            var targetBlock = transcript.Blocks[target.BlockIndex];
            if (targetBlock.Id != target.BlockId ||
                targetBlock is not ITextSelectable selectable ||
                !selectable.CanSelect)
            {
                continue;
            }

            var selectableStart = targetBlock is PromptBlock prompt ? prompt.PromptPrefixLength : 0;
            if (selectable.TextLength <= selectableStart)
            {
                continue;
            }

            var nextIndex = target.Start + Math.Min(col, target.Length);
            nextIndex = NormalizePromptPrefix(transcript, target.BlockId, nextIndex);
            nextIndex = Math.Clamp(nextIndex, selectableStart, selectable.TextLength);

            moved = new SelectionPosition(target.BlockId, nextIndex);
            return moved != caret;
        }

        return false;
    }

    private static bool TryFindLineForCaret(
        HitTestMap map,
        SelectionPosition caret,
        out HitTestLine line,
        out int col)
    {
        foreach (var candidate in map.Lines)
        {
            if (candidate.BlockId != caret.BlockId)
            {
                continue;
            }

            var start = candidate.Start;
            var endExclusive = candidate.Start + candidate.Length;
            if (caret.Index >= start && caret.Index < endExclusive)
            {
                line = candidate;
                col = caret.Index - start;
                return true;
            }
        }

        foreach (var candidate in map.Lines)
        {
            if (candidate.BlockId != caret.BlockId)
            {
                continue;
            }

            var start = candidate.Start;
            var endInclusive = candidate.Start + candidate.Length;
            if (caret.Index == endInclusive)
            {
                line = candidate;
                col = caret.Index - start;
                return true;
            }
        }

        line = default;
        col = 0;
        return false;
    }

    private static bool TryNormalizeRange(
        Transcript transcript,
        SelectionRange range,
        out SelectionPosition start,
        out SelectionPosition end,
        out int startBlockIndex,
        out int endBlockIndex)
    {
        start = range.Anchor;
        end = range.Caret;

        startBlockIndex = -1;
        endBlockIndex = -1;

        if (!TryFindBlockIndex(transcript, start.BlockId, out startBlockIndex) ||
            !TryFindBlockIndex(transcript, end.BlockId, out endBlockIndex))
        {
            return false;
        }

        if (startBlockIndex > endBlockIndex || (startBlockIndex == endBlockIndex && start.Index > end.Index))
        {
            (start, end) = (end, start);
            (startBlockIndex, endBlockIndex) = (endBlockIndex, startBlockIndex);
        }

        return true;
    }

    private static bool TryFindBlockIndex(Transcript transcript, BlockId id, out int blockIndex)
    {
        var blocks = transcript.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == id)
            {
                blockIndex = i;
                return true;
            }
        }

        blockIndex = -1;
        return false;
    }

    private void HealStateAfterTranscriptChange(Transcript transcript)
    {
        // Host mutates the transcript outside the reducer (e.g., job prompts replacing/removing blocks).
        // Self-heal references so DEBUG invariants don't explode on the next input event.

        if (_state.Focused is { } focused && !ContainsBlock(transcript, focused))
        {
            _state.Focused = _state.LastPromptId ?? FindLastBlockId(transcript);
        }

        if (_state.MouseCaptured is { } captured && !ContainsBlock(transcript, captured))
        {
            _state.MouseCaptured = null;
            _state.IsSelecting = false;
        }

        if (_state.Selection is { } range &&
            (!ContainsBlock(transcript, range.Anchor.BlockId) || !ContainsBlock(transcript, range.Caret.BlockId)))
        {
            _state.Selection = null;
            _state.IsSelecting = false;
            _state.MouseCaptured = null;
        }

        if (_state.SelectionCaret is { } caret && !ContainsBlock(transcript, caret.BlockId))
        {
            _state.SelectionCaret = null;
        }
    }

    private static BlockId? FindLastBlockId(Transcript transcript)
    {
        var blocks = transcript.Blocks;
        return blocks.Count == 0 ? null : blocks[^1].Id;
    }

    [Conditional("DEBUG")]
    private void ValidateState(Transcript transcript)
    {
        // Invariants (debug-only):
        // 1) MouseCaptured <-> IsSelecting must match (capture is used only for selection today)
        // 2) IsSelecting implies Selection != null
        // 3) Focused != null must exist in transcript
        // 4) If Selection != null, anchor/caret block ids must exist in transcript
        // 5) Prompt prefix rule: selection indices into PromptBlock must be >= PromptPrefixLength
        // 6) If not selecting, selection must not be empty (anchor != caret)

        if (_state.IsSelecting != (_state.MouseCaptured is not null))
        {
            throw new InvalidOperationException($"Interaction invariant failed (capture/select mismatch). {FormatStateForDebug()}");
        }

        if (_state.IsSelecting && _state.Selection is null)
        {
            throw new InvalidOperationException($"Interaction invariant failed (selecting requires selection). {FormatStateForDebug()}");
        }

        if (_state.Focused is { } focused && !ContainsBlock(transcript, focused))
        {
            throw new InvalidOperationException($"Interaction invariant failed (focused block missing). {FormatStateForDebug()}");
        }

        if (_state.Selection is { } range)
        {
            if (!ContainsBlock(transcript, range.Anchor.BlockId) || !ContainsBlock(transcript, range.Caret.BlockId))
            {
                throw new InvalidOperationException($"Interaction invariant failed (selection block missing). {FormatStateForDebug()}");
            }

            if (TryGetPrompt(transcript, range.Anchor.BlockId, out var anchorPrompt) &&
                range.Anchor.Index < anchorPrompt.PromptPrefixLength)
            {
                throw new InvalidOperationException($"Interaction invariant failed (anchor in prompt prefix). {FormatStateForDebug()}");
            }

            if (TryGetPrompt(transcript, range.Caret.BlockId, out var caretPrompt) &&
                range.Caret.Index < caretPrompt.PromptPrefixLength)
            {
                throw new InvalidOperationException($"Interaction invariant failed (caret in prompt prefix). {FormatStateForDebug()}");
            }

            if (!_state.IsSelecting && range.Anchor == range.Caret)
            {
                throw new InvalidOperationException($"Interaction invariant failed (empty selection while not selecting). {FormatStateForDebug()}");
            }
        }
    }

    private static bool ContainsBlock(Transcript transcript, BlockId id)
    {
        foreach (var block in transcript.Blocks)
        {
            if (block.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    private string FormatStateForDebug()
    {
        var focused = _state.Focused?.ToString() ?? "null";
        var captured = _state.MouseCaptured?.ToString() ?? "null";
        var selection = _state.Selection?.ToString() ?? "null";
        return $"State: Focused={focused} Captured={captured} IsSelecting={_state.IsSelecting} Selection={selection}";
    }
}
