namespace Cycon.Core.Settings;

public sealed class ActivityIndicatorSettings
{
    public double ShowDelaySeconds { get; init; } = 0.20;

    // Animation cadence for renderer-only overlays.
    public int AnimationFps { get; init; } = 30;

    // Indeterminate indicator: pulsing full-cell caret block.
    public double PulsePeriodSeconds { get; init; } = 1.0;
    public byte PulseMinAlpha { get; init; } = 0x30;
    public byte PulseMaxAlpha { get; init; } = 0xD0;

    // Determinate indicator: full-cell caret block that fills up (bottom-up).
    public byte ProgressFillAlpha { get; init; } = 0xD0;
    public byte ProgressTrackAlpha { get; init; } = 0x30;
}
