using System.Collections.Generic;

namespace Cycon.Rendering.Glyphs;

public sealed class GlyphAtlas
{
    public GlyphAtlas(
        int width,
        int height,
        int cellWidthPx,
        int cellHeightPx,
        int baselinePx,
        IReadOnlyDictionary<int, GlyphMetrics> metrics,
        byte[] pixels)
    {
        Width = width;
        Height = height;
        CellWidthPx = cellWidthPx;
        CellHeightPx = cellHeightPx;
        BaselinePx = baselinePx;
        Metrics = metrics;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int CellWidthPx { get; }
    public int CellHeightPx { get; }
    public int BaselinePx { get; }
    public IReadOnlyDictionary<int, GlyphMetrics> Metrics { get; }
    public byte[] Pixels { get; }

    public bool TryGetMetrics(int codepoint, out GlyphMetrics metrics) => Metrics.TryGetValue(codepoint, out metrics);
}
