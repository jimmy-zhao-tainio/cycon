namespace Cycon.Rendering.Glyphs;

public readonly record struct GlyphMetrics(
    int Codepoint,
    int Width,
    int Height,
    int BearingX,
    int BearingY,
    int AdvanceX,
    int AtlasX,
    int AtlasY);
