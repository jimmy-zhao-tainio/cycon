using Cycon.Backends.Abstractions.Rendering;

namespace Cycon.Core.Fonts;

public interface IConsoleFont
{
    FontMetrics Metrics { get; }
    GlyphAtlasData Atlas { get; }
    GlyphRect MapGlyph(int codepoint);
}

