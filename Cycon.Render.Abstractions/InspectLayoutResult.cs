namespace Cycon.Render;

public readonly record struct PointPx(int X, int Y);

public readonly record struct InspectLayoutResult(
    RectPx OuterRect,
    RectPx ContentRect,
    RectPx? TopPanelRect,
    RectPx? BottomPanelRect,
    RectPx? LeftPanelRect,
    RectPx? RightPanelRect,
    PointPx GridOriginPx,
    int CellW,
    int CellH);
