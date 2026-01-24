namespace Cycon.Render;

public readonly record struct BlockRenderContext(
    RectPx ViewportRectPx,
    double TimeSeconds,
    RenderTheme Theme,
    TextMetrics TextMetrics,
    Scene3DRenderSettings Scene3D,
    int FramebufferWidthPx,
    int FramebufferHeightPx);
