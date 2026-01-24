using System;
using Cycon.Core.Transcript;

namespace Cycon.Core.Transcript.Blocks;

public sealed class PromptBlock : IBlock, ITextSelectable, ITextEditable, IRunnableBlock, IStoppableBlock
{
    public PromptBlock(BlockId id, string prompt = "> ")
        : this(id, prompt, owner: null)
    {
    }

    public PromptBlock(BlockId id, string prompt, PromptOwner? owner)
    {
        Id = id;
        Prompt = prompt;
        Owner = owner;
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Prompt;

    public string Prompt { get; set; }
    public PromptOwner? Owner { get; }
    public int PromptPrefixLength => Prompt.Length;
    public string Input { get; set; } = string.Empty;
    public int CaretIndex { get; set; }

    public BlockRunState State { get; private set; } = BlockRunState.Running;
    public StopLevel? StopRequestedLevel { get; private set; }

    public bool CanSelect => true;
    public int TextLength => Prompt.Length + Input.Length;

    public bool CanStop => Owner is not null && State == BlockRunState.Running;

    public void RequestStop(StopLevel level)
    {
        if (!CanStop)
        {
            return;
        }

        StopRequestedLevel = level;
        State = BlockRunState.Cancelled;
    }

    public void Tick(TimeSpan dt)
    {
        // No-op for now. Prompt input is driven by host input events.
    }

    public string ExportText(int start, int length)
    {
        var text = Prompt + Input;
        if (start < 0 || length < 0 || start + length > text.Length)
        {
            throw new ArgumentOutOfRangeException();
        }

        var selectableStart = Prompt.Length;
        var selectableEnd = text.Length;
        var startClamped = Math.Clamp(start, selectableStart, selectableEnd);
        var endClamped = Math.Clamp(start + length, selectableStart, selectableEnd);
        var clampedLength = endClamped - startClamped;
        return clampedLength <= 0 ? string.Empty : text.Substring(startClamped, clampedLength);
    }

    public void InsertText(string s)
    {
        if (State != BlockRunState.Running)
        {
            return;
        }

        if (string.IsNullOrEmpty(s))
        {
            return;
        }

        var caret = Math.Clamp(CaretIndex, 0, Input.Length);
        Input = Input.Insert(caret, s);
        CaretIndex = caret + s.Length;
    }

    public void Backspace()
    {
        if (State != BlockRunState.Running)
        {
            return;
        }

        if (CaretIndex <= 0 || Input.Length == 0)
        {
            return;
        }

        var caret = Math.Clamp(CaretIndex, 0, Input.Length);
        Input = Input.Remove(caret - 1, 1);
        CaretIndex = caret - 1;
    }

    public void MoveCaret(int delta)
    {
        if (State != BlockRunState.Running)
        {
            return;
        }

        SetCaret(CaretIndex + delta);
    }

    public void SetCaret(int index)
    {
        if (State != BlockRunState.Running)
        {
            return;
        }

        CaretIndex = Math.Clamp(index, 0, Input.Length);
    }
}

public readonly record struct PromptOwner(long OwnerId, long PromptId);
