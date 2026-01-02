namespace Cycon.Render;

public readonly record struct Scene3DRenderSettings(
    int StlDebugMode,
    float SolidAmbient,
    float SolidDiffuseStrength,
    float ToneGamma,
    float ToneGain,
    float ToneLift,
    float VignetteStrength,
    float VignetteInner,
    float VignetteOuter,
    bool ShowVertexDots,
    int VertexDotMaxVertices,
    int VertexDotMaxDots);
