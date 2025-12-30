namespace Cycon.Layout;

public sealed class LayoutSettings
{
    public int CellWidthPx { get; set; }
    public int CellHeightPx { get; set; }
    public int BaselinePx { get; set; }
    public PaddingPolicy PaddingPolicy { get; set; } = PaddingPolicy.Center;
}

public enum PaddingPolicy
{
    None,
    Center
}
