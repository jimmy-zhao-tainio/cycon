namespace Cycon.Core.Transcript.Blocks;

public sealed class Scene3DBlock : IBlock
{
    public Scene3DBlock(BlockId id)
    {
        Id = id;
    }

    public BlockId Id { get; }
    public BlockKind Kind => BlockKind.Scene3D;
}

