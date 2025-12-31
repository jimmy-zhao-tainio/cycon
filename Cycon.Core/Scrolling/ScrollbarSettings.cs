namespace Cycon.Core.Scrolling;

public sealed class ScrollbarSettings
{
    public int ThicknessPx { get; set; } = 8;
    public int MarginPx { get; set; } = 0;
    public int MinThumbPx { get; set; } = 18;

    public float TrackOpacityIdle { get; set; } = 0.12f;
    public float ThumbOpacityIdle { get; set; } = 0.25f;
    public float ThumbOpacityHover { get; set; } = 0.45f;
    public float ThumbOpacityDrag { get; set; } = 0.65f;

    public int FadeInMs { get; set; } = 120;
    public int FadeOutMs { get; set; } = 260;
    public int AutoHideDelayMs { get; set; } = 900;
}
