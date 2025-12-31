namespace Cycon.Core.Fonts;

public readonly record struct GlyphRect(
    int Codepoint,
    int Width,
    int Height,
    int BearingX,
    int BearingY,
    int AdvanceX,
    int AtlasX,
    int AtlasY);

