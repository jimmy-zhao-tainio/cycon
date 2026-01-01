namespace Cycon.Core.Fonts;

public readonly record struct FontMetrics
{
    public FontMetrics(int cellWidthPx, int cellHeightPx, int baselineOffsetPx)
        : this(
            cellWidthPx,
            cellHeightPx,
            lineHeightPx: cellHeightPx,
            baselineOffsetPx: baselineOffsetPx,
            underlineTopOffsetPx: -1,
            underlineThicknessPx: 1)
    {
    }

    public FontMetrics(
        int cellWidthPx,
        int cellHeightPx,
        int lineHeightPx,
        int baselineOffsetPx,
        int underlineTopOffsetPx,
        int underlineThicknessPx)
    {
        CellWidthPx = cellWidthPx;
        CellHeightPx = cellHeightPx;
        LineHeightPx = lineHeightPx;
        BaselineOffsetPx = baselineOffsetPx;
        UnderlineTopOffsetPx = underlineTopOffsetPx;
        UnderlineThicknessPx = underlineThicknessPx;
    }

    public int CellWidthPx { get; }

    public int CellHeightPx { get; }

    public int LineHeightPx { get; }

    public int BaselineOffsetPx { get; }

    // Kept for compatibility with existing call sites.
    public int BaselinePx => BaselineOffsetPx;

    // Relative to baseline; can be negative depending on the font's baseline placement.
    public int UnderlineTopOffsetPx { get; }

    public int UnderlineThicknessPx { get; }

    public int GetBaselineY(int lineTopY) => lineTopY + BaselineOffsetPx;

    public int GetUnderlineTopY(int lineTopY) => GetBaselineY(lineTopY) + UnderlineTopOffsetPx;

    public int GetUnderlineBottomExclusiveY(int lineTopY) => GetUnderlineTopY(lineTopY) + UnderlineThicknessPx;
}
