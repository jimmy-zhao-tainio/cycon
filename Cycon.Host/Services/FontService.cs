using System;
using System.IO;
using Cycon.Layout;
using Cycon.Rendering.Fonts;
using Cycon.Rendering.Glyphs;

namespace Cycon.Host.Services;

public sealed class FontService
{
    public GlyphAtlas LoadVgaAtlas(LayoutSettings layoutSettings)
    {
        var atlasPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "vga-8x16.bmp");
        var atlas = VgaCp4378x16Font.LoadAtlas(atlasPath);
        layoutSettings.CellWidthPx = atlas.CellWidthPx;
        layoutSettings.CellHeightPx = atlas.CellHeightPx;
        layoutSettings.BaselinePx = atlas.BaselinePx;
        return atlas;
    }
}
