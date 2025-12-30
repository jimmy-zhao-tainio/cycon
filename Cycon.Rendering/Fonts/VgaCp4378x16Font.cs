using System.Collections.Generic;
using Cycon.Rendering.Glyphs;

namespace Cycon.Rendering.Fonts;

public static class VgaCp4378x16Font
{
    public const int AtlasWidthPx = 256;
    public const int AtlasHeightPx = 128;
    public const int CellWidthPx = 8;
    public const int CellHeightPx = 16;
    public const int BaselinePx = 15;

    public static GlyphAtlas LoadAtlas(string bmpPath)
    {
        var (width, height, rgba) = BmpLoader.LoadRgba24(bmpPath);
        if (width != AtlasWidthPx || height != AtlasHeightPx)
        {
            throw new InvalidOperationException(
                $"Unexpected atlas size for '{bmpPath}': got {width}x{height}, expected {AtlasWidthPx}x{AtlasHeightPx}.");
        }

        var metrics = new Dictionary<int, GlyphMetrics>(256);
        for (var cp = 0; cp < 256; cp++)
        {
            var col = cp & 31;
            var rowFromTop = cp >> 5;

            var atlasX = col * CellWidthPx;
            var atlasY = rowFromTop * CellHeightPx;

            metrics[cp] = new GlyphMetrics(
                cp,
                CellWidthPx,
                CellHeightPx,
                0,
                BaselinePx,
                CellWidthPx,
                atlasX,
                atlasY);
        }

        return new GlyphAtlas(
            AtlasWidthPx,
            AtlasHeightPx,
            CellWidthPx,
            CellHeightPx,
            BaselinePx,
            metrics,
            rgba);
    }
}
