namespace Cycon.Rendering.Glyphs;

public sealed class GlyphBitmap
{
    public GlyphBitmap(int codepoint, int width, int height, int bearingX, int bearingY, int advanceX, byte[] pixels)
    {
        Codepoint = codepoint;
        Width = width;
        Height = height;
        BearingX = bearingX;
        BearingY = bearingY;
        AdvanceX = advanceX;
        Pixels = pixels;
    }

    public int Codepoint { get; }
    public int Width { get; }
    public int Height { get; }
    public int BearingX { get; }
    public int BearingY { get; }
    public int AdvanceX { get; }
    public byte[] Pixels { get; }
}
