using System;
using System.Numerics;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Render;

namespace Extensions.Inspect.Blocks;

public sealed partial class StlBlock : IScene3DViewBlock, IMouseFocusableViewportBlock, IRenderBlock, IMeasureBlock, IMesh3DResourceOwner
{
    private const float DefaultHorizontalFovDegrees = 80f;
    private const float FitPaddingMultiplier = 0.75f;

    private float _lastProjectionAspect = float.NaN;
    private float _lastHorizontalFovDegrees = DefaultHorizontalFovDegrees;
    private float _lastVerticalFovRadians = float.NaN;

    public StlBlock(
        BlockId id,
        string filePath,
        float[] vertexData,
        int vertexCount,
        int triangleCount,
        Bounds bounds)
    {
        Id = id;
        FilePath = filePath;
        VertexData = vertexData;
        VertexCount = vertexCount;
        TriangleCount = triangleCount;
        MeshBounds = bounds;

        PreferredAspectRatio = 16.0 / 9.0;

        _lastProjectionAspect = (float)(PreferredAspectRatio <= 0 ? (16.0 / 9.0) : PreferredAspectRatio);
        _lastVerticalFovRadians = ComputeVerticalFovRadians(_lastHorizontalFovDegrees, _lastProjectionAspect);

        CenterDir = Vector3.Normalize(new Vector3(0, MathF.Sin(0.35f), MathF.Cos(0.35f)));
        FocusDistance = ComputeFitDistance(bounds.Radius, _lastVerticalFovRadians);
        CameraPos = bounds.Center - (CenterDir * FocusDistance);
    }

    public BlockId Id { get; }

    public BlockKind Kind => BlockKind.Scene3D;

    public int MeshId => Id.Value;

    public bool HasMouseFocus { get; set; }

    public string FilePath { get; }

    /// <summary>
    /// Interleaved triangle soup: (x,y,z,nx,ny,nz) per vertex, 3 vertices per triangle.
    /// </summary>
    public float[] VertexData { get; }

    public int VertexCount { get; }

    public int TriangleCount { get; }

    public Bounds MeshBounds { get; }

    public double PreferredAspectRatio { get; set; }

    public Vector3 CameraPos { get; set; }

    public Vector3 CenterDir { get; set; }

    public float FocusDistance { get; set; }

    public float BoundsRadius => MeshBounds.Radius;

    public void ResetCameraToFit()
    {
        var aspect = float.IsFinite(_lastProjectionAspect)
            ? _lastProjectionAspect
            : (float)(PreferredAspectRatio <= 0 ? (16.0 / 9.0) : PreferredAspectRatio);
        _lastVerticalFovRadians = ComputeVerticalFovRadians(_lastHorizontalFovDegrees, aspect);
        CenterDir = Vector3.Normalize(new Vector3(0, MathF.Sin(0.35f), MathF.Cos(0.35f)));
        FocusDistance = ComputeFitDistance(MeshBounds.Radius, _lastVerticalFovRadians);
        CameraPos = MeshBounds.Center - (CenterDir * FocusDistance);
    }

    public BlockSize Measure(in BlockMeasureContext ctx)
    {
        var width = Math.Max(0, ctx.ContentWidthPx);
        var aspect = PreferredAspectRatio <= 0 ? (16.0 / 9.0) : PreferredAspectRatio;
        var idealHeight = (int)Math.Round(width / aspect);
        return new BlockSize(width, idealHeight);
    }

    private static float ComputeFitDistance(float radius, float vfovRadians)
    {
        radius = MathF.Max(radius, 0.0001f);
        var d = radius / MathF.Tan(vfovRadians * 0.5f);
        return d * FitPaddingMultiplier;
    }

    private static float ComputeVerticalFovRadians(float horizontalFovDegrees, float aspect)
    {
        aspect = MathF.Max(0.0001f, aspect);
        var hfov = horizontalFovDegrees * (MathF.PI / 180f);
        var half = hfov * 0.5f;
        return 2f * MathF.Atan(MathF.Tan(half) / aspect);
    }

    public readonly record struct Bounds(Vector3 Min, Vector3 Max)
    {
        public Vector3 Center => (Min + Max) * 0.5f;

        public float Radius
        {
            get
            {
                var extents = (Max - Min) * 0.5f;
                return extents.Length();
            }
        }
    }
}
