using System;
using System.IO;
using Cycon.Core.Fonts;
using Cycon.Layout;
using Cycon.Rendering.Fonts;

namespace Cycon.Host.Services;

public sealed class FontService
{
    public IConsoleFont CreateDefaultFont(LayoutSettings layoutSettings)
    {
        var atlasPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "vga-8x16.bmp");
        var font = new VgaCp4378x16Font(atlasPath);
        layoutSettings.CellWidthPx = font.Metrics.CellWidthPx;
        layoutSettings.CellHeightPx = font.Metrics.CellHeightPx;
        layoutSettings.BaselinePx = font.Metrics.BaselinePx;
        return font;
    }
}
