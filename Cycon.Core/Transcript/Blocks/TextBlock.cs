using System;

namespace Cycon.Core.Transcript.Blocks;

public sealed class TextBlock : IBlock, ITextSelectable
{
    public TextBlock(BlockId id, string text)
    {
        Id = id;
        Text = text;
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Text;
    public string Text { get; }

    public bool CanSelect => true;
    public int TextLength => Text.Length;

    public string ExportText(int start, int length)
    {
        if (start < 0 || length < 0 || start + length > Text.Length)
        {
            throw new ArgumentOutOfRangeException();
        }

        return length == 0 ? string.Empty : Text.Substring(start, length);
    }
}
