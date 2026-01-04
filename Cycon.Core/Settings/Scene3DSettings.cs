namespace Cycon.Core.Settings;

public enum StlDebugMode
{
    Normal,
    DoubleSided,
    Unlit,
    FaceNormals
}

public sealed class Scene3DSettings
{
    public float HorizontalFovDegrees { get; set; } = 80f;

    public StlDebugMode StlDebugMode { get; set; } = StlDebugMode.DoubleSided;

    public float SolidAmbient { get; set; } = 0.62f;

    public float SolidDiffuseStrength { get; set; } = 0.32f;

    public float ToneGamma { get; set; } = 1.10f;

    public float ToneGain { get; set; } = 1.00f;

    public float ToneLift { get; set; } = 0.00f;

    public float VignetteStrength { get; set; } = 0.10f;

    public float VignetteInner { get; set; } = 0.55f;

    public float VignetteOuter { get; set; } = 0.95f;

    public bool ShowVertexDots { get; set; } = false;

    public int VertexDotMaxVertices { get; set; } = 50_000;

    public int VertexDotMaxDots { get; set; } = 2_000;

    public float OrbitSensitivity { get; set; } = 0.008f;

    public float PanSensitivity { get; set; } = 0.0025f;

    public float MouseSmoothingTauSeconds { get; set; } = 0.06f;

    public float FreeflyLookTauSeconds { get; set; } = 0.05f;

    public float FreeflyLookSensitivity { get; set; } = 0.006f;

    public float ZoomSensitivity { get; set; } = 0.10f;

    public float KeyboardPanSpeed { get; set; } = 0.80f;

    public float KeyboardDollySpeed { get; set; } = 0.80f;

    public bool InvertOrbitX { get; set; } = true;

    public bool InvertOrbitY { get; set; } = false;

    public bool InvertPanX { get; set; } = true;

    public bool InvertPanY { get; set; } = false;

    public bool InvertZoom { get; set; } = false;
}
