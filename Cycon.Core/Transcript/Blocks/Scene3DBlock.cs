namespace Cycon.Core.Transcript.Blocks;

public sealed class Scene3DBlock : IBlock
{
    public Scene3DBlock(int preferredHeightPx)
    {
        PreferredHeightPx = preferredHeightPx;
    }

    public int PreferredHeightPx { get; }
}
