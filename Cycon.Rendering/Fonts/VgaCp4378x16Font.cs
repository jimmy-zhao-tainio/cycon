using System;
using System.Collections.Generic;
using Cycon.Backends.Abstractions.Rendering;
using Cycon.Core.Fonts;

namespace Cycon.Rendering.Fonts;

public sealed class VgaCp4378x16Font : IConsoleFont
{
    public const int AtlasWidthPx = 256;
    public const int AtlasHeightPx = 128;
    public const int CellWidthPx = 8;
    public const int CellHeightPx = 16;
    public const int BaselinePx = 15;

    private readonly IReadOnlyDictionary<int, GlyphRect> _glyphs;

    public VgaCp4378x16Font(string bmpPath)
    {
        var (width, height, rgba) = BmpLoader.LoadRgba24(bmpPath);
        if (width != AtlasWidthPx || height != AtlasHeightPx)
        {
            throw new InvalidOperationException(
                $"Unexpected atlas size for '{bmpPath}': got {width}x{height}, expected {AtlasWidthPx}x{AtlasHeightPx}.");
        }

        Metrics = new FontMetrics(CellWidthPx, CellHeightPx, BaselinePx);

        var glyphs = new Dictionary<int, GlyphRect>(256);
        var metrics = new Dictionary<int, GlyphMetricsData>(256);
        for (var cp = 0; cp < 256; cp++)
        {
            var col = cp & 31;
            var rowFromTop = cp >> 5;

            var atlasX = col * Metrics.CellWidthPx;
            var atlasY = rowFromTop * Metrics.CellHeightPx;

            var glyph = new GlyphRect(
                cp,
                Metrics.CellWidthPx,
                Metrics.CellHeightPx,
                0,
                Metrics.BaselinePx,
                Metrics.CellWidthPx,
                atlasX,
                atlasY);

            glyphs[cp] = glyph;
            metrics[cp] = new GlyphMetricsData(
                cp,
                glyph.Width,
                glyph.Height,
                glyph.BearingX,
                glyph.BearingY,
                glyph.AdvanceX,
                glyph.AtlasX,
                glyph.AtlasY);
        }

        _glyphs = glyphs;
        Atlas = new GlyphAtlasData(
            AtlasWidthPx,
            AtlasHeightPx,
            Metrics.CellWidthPx,
            Metrics.CellHeightPx,
            Metrics.BaselinePx,
            metrics,
            rgba);
    }

    public FontMetrics Metrics { get; }

    public GlyphAtlasData Atlas { get; }

    public GlyphRect MapGlyph(int codepoint)
    {
        // CP437 sheet is 0..255; for other codepoints use '?'.
        if (codepoint < 0 || codepoint > 255)
        {
            codepoint = '?';
        }

        if (_glyphs.TryGetValue(codepoint, out var rect))
        {
            return rect;
        }

        return _glyphs['?'];
    }
}
