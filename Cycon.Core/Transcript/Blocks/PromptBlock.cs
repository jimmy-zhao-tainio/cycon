using System;

namespace Cycon.Core.Transcript.Blocks;

public sealed class PromptBlock : IBlock, ITextSelectable, ITextEditable
{
    public PromptBlock(BlockId id, string prompt = "> ")
    {
        Id = id;
        Prompt = prompt;
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Prompt;

    public string Prompt { get; }
    public int PromptPrefixLength => Prompt.Length;
    public string Input { get; set; } = string.Empty;
    public int CaretIndex { get; set; }

    public bool CanSelect => true;
    public int TextLength => Prompt.Length + Input.Length;

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
        SetCaret(CaretIndex + delta);
    }

    public void SetCaret(int index)
    {
        CaretIndex = Math.Clamp(index, 0, Input.Length);
    }
}
