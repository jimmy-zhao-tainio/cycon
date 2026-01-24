using System;
using System.Collections.Generic;
using Cycon.Core.Styling;

namespace Cycon.Core.Transcript.Blocks;

public sealed class RichTextBlock : IBlock, ITextSelectable
{
    private readonly IReadOnlyList<RichTextActionSpan> _actions;

    public RichTextBlock(BlockId id, string text, ConsoleTextStream stream, IReadOnlyList<RichTextActionSpan> actions)
    {
        Id = id;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Stream = stream;
        _actions = actions ?? Array.Empty<RichTextActionSpan>();
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Text;
    public string Text { get; }
    public ConsoleTextStream Stream { get; }
    public IReadOnlyList<RichTextActionSpan> Actions => _actions;

    public bool CanSelect => true;
    public int TextLength => Text.Length;

    public bool TryGetClickAction(int charIndex, out string commandText)
    {
        commandText = string.Empty;
        if ((uint)charIndex >= (uint)Text.Length)
        {
            return false;
        }

        for (var i = 0; i < _actions.Count; i++)
        {
            var span = _actions[i];
            if (charIndex < span.Start)
            {
                continue;
            }

            if (charIndex >= span.Start + span.Length)
            {
                continue;
            }

            commandText = span.CommandText;
            return !string.IsNullOrEmpty(commandText);
        }

        return false;
    }

    public string ExportText(int start, int length)
    {
        if (start < 0 || length < 0 || start + length > Text.Length)
        {
            throw new ArgumentOutOfRangeException();
        }

        return length == 0 ? string.Empty : Text.Substring(start, length);
    }
}

public readonly record struct RichTextActionSpan(int Start, int Length, string CommandText);
