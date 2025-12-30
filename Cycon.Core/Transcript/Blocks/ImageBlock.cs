namespace Cycon.Core.Transcript.Blocks;

public sealed class ImageBlock : IBlock
{
    public ImageBlock(BlockId id)
    {
        Id = id;
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Image;
}

