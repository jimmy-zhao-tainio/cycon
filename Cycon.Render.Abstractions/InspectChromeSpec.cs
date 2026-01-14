namespace Cycon.Render;

public enum InspectChromeStyleId
{
    DefaultFrame,
    Frame2Px,
    PanelBg
}

public enum InspectEdge
{
    Top,
    Bottom,
    Left,
    Right
}

public readonly record struct InspectPanelSpec(InspectEdge Edge, int SizeCells, bool DrawSeparator);

public readonly record struct InspectTextRowSpec(
    InspectEdge Edge,
    int RowIndex,
    string? LeftKey,
    string? CenterKey,
    string? RightKey);

public readonly record struct InspectChromeSpec(
    bool Enabled,
    InspectChromeStyleId StyleId,
    int OuterBorderPx,
    InspectPanelSpec[] Panels,
    InspectTextRowSpec[] TextRows);
