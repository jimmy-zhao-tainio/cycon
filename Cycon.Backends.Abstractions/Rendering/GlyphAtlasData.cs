using System.Collections.Generic;

namespace Cycon.Backends.Abstractions.Rendering;

public sealed class GlyphAtlasData
{
    public GlyphAtlasData(
        int width,
        int height,
        int cellWidthPx,
        int cellHeightPx,
        int baselinePx,
        IReadOnlyDictionary<int, GlyphMetricsData> metrics,
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
    public IReadOnlyDictionary<int, GlyphMetricsData> Metrics { get; }
    public byte[] Pixels { get; }

    public bool TryGetMetrics(int codepoint, out GlyphMetricsData metrics) => Metrics.TryGetValue(codepoint, out metrics);
}

public readonly record struct GlyphMetricsData(
    int Codepoint,
    int Width,
    int Height,
    int BearingX,
    int BearingY,
    int AdvanceX,
    int AtlasX,
    int AtlasY);
