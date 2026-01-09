namespace Cycon.Core.Transcript;

public enum BlockChromeStyle
{
    Frame2Px,
    PanelBg
}

public readonly record struct BlockChromeSpec(bool Enabled, BlockChromeStyle Style, int PaddingPx, int BorderPx)
{
    public static BlockChromeSpec Disabled => new(false, BlockChromeStyle.Frame2Px, 0, 0);

    public static BlockChromeSpec ViewDefault => new(true, BlockChromeStyle.PanelBg, 4, 0);
}
