namespace Cycon.Core.Transcript.Blocks;

public sealed class TextBlock : IBlock
{
    public TextBlock(BlockId id, string text)
    {
        Id = id;
        Text = text;
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Text;
    public string Text { get; }
}
