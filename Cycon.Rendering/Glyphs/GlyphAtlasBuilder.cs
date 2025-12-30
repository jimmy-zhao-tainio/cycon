using System;
using System.Collections.Generic;

namespace Cycon.Rendering.Glyphs;

public sealed class GlyphAtlasBuilder
{
    private const int Padding = 1;

    public GlyphAtlas Build(IReadOnlyList<GlyphBitmap> glyphs, int cellWidthPx, int cellHeightPx, int baselinePx)
    {
        if (glyphs.Count == 0)
        {
            return new GlyphAtlas(0, 0, cellWidthPx, cellHeightPx, baselinePx, new Dictionary<int, GlyphMetrics>(), Array.Empty<byte>());
        }

        var maxGlyphWidth = 0;
        for (var i = 0; i < glyphs.Count; i++)
        {
            if (glyphs[i].Width > maxGlyphWidth)
            {
                maxGlyphWidth = glyphs[i].Width;
            }
        }

        var atlasWidth = Math.Max(256, maxGlyphWidth + (Padding * 2));
        var placements = new List<(GlyphBitmap Glyph, int X, int Y)>();

        var x = Padding;
        var y = Padding;
        var rowHeight = 0;

        foreach (var glyph in glyphs)
        {
            if (x + glyph.Width + Padding > atlasWidth)
            {
                x = Padding;
                y += rowHeight + Padding;
                rowHeight = 0;
            }

            placements.Add((glyph, x, y));
            x += glyph.Width + Padding;
            rowHeight = Math.Max(rowHeight, glyph.Height);
        }

        var atlasHeight = y + rowHeight + Padding;
        var pixels = new byte[atlasWidth * atlasHeight];
        var metrics = new Dictionary<int, GlyphMetrics>();

        foreach (var placement in placements)
        {
            var glyph = placement.Glyph;

            if (glyph.Width > 0 && glyph.Height > 0)
            {
                var expectedLength = (long)glyph.Width * glyph.Height;
                if (expectedLength <= 0 || expectedLength > glyph.Pixels.Length)
                {
                    metrics[glyph.Codepoint] = new GlyphMetrics(
                        glyph.Codepoint,
                        0,
                        0,
                        glyph.BearingX,
                        glyph.BearingY,
                        glyph.AdvanceX,
                        placement.X,
                        placement.Y);
                    continue;
                }

                for (var row = 0; row < glyph.Height; row++)
                {
                    Buffer.BlockCopy(
                        glyph.Pixels,
                        row * glyph.Width,
                        pixels,
                        (placement.Y + row) * atlasWidth + placement.X,
                        glyph.Width);
                }
            }

            metrics[glyph.Codepoint] = new GlyphMetrics(
                glyph.Codepoint,
                glyph.Width,
                glyph.Height,
                glyph.BearingX,
                glyph.BearingY,
                glyph.AdvanceX,
                placement.X,
                placement.Y);
        }

        return new GlyphAtlas(atlasWidth, atlasHeight, cellWidthPx, cellHeightPx, baselinePx, metrics, pixels);
    }
}
