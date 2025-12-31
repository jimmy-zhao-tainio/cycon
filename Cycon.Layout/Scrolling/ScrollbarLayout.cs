namespace Cycon.Layout.Scrolling;

public readonly record struct ScrollbarLayout(
    bool IsScrollable,
    PxRect TrackRectPx,
    PxRect ThumbRectPx,
    PxRect HitTrackRectPx,
    PxRect HitThumbRectPx);

