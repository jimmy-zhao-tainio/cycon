namespace Cycon.Core.Transcript.Blocks;

public sealed class ImageBlock : IBlock
{
    public ImageBlock(int widthPx, int heightPx)
    {
        WidthPx = widthPx;
        HeightPx = heightPx;
    }

    public int WidthPx { get; }
    public int HeightPx { get; }
}
