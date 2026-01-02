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

    public float ZoomSensitivity { get; set; } = 0.10f;

    public bool InvertOrbitX { get; set; } = false;

    public bool InvertOrbitY { get; set; } = true;

    public bool InvertPanX { get; set; } = true;

    public bool InvertPanY { get; set; } = false;

    public bool InvertZoom { get; set; } = false;
}
