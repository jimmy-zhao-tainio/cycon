namespace Cycon.Layout;

public sealed class LayoutSettings
{
    public int CellWidthPx { get; set; }
    public int CellHeightPx { get; set; }
    public int BaselinePx { get; set; }

    public int BorderLeftRightPx { get; set; }
    public int BorderTopBottomPx { get; set; }
    public int RightGutterPx { get; set; }

    public int BorderPx
    {
        get => Math.Min(BorderLeftRightPx, BorderTopBottomPx);
        set
        {
            BorderLeftRightPx = value;
            BorderTopBottomPx = value;
        }
    }
    public PaddingPolicy PaddingPolicy { get; set; } = PaddingPolicy.Center;
}

public enum PaddingPolicy
{
    None,
    Center
}
