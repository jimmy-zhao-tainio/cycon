using System;
using System.Numerics;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Render;

namespace Extensions.Deconstruction.Blocks;

public sealed partial class StlBlock : IScene3DViewBlock, IRenderBlock, IMeasureBlock
{
    private const float FitFovDegrees = 60f;
    private const float FitPaddingMultiplier = 0.75f;

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

        Target = bounds.Center;
        YawRadians = 0f;
        PitchRadians = 0.35f;
        Distance = ComputeFitDistance(bounds.Radius);
    }

    public BlockId Id { get; }

    public BlockKind Kind => BlockKind.Scene3D;

    public string FilePath { get; }

    /// <summary>
    /// Interleaved triangle soup: (x,y,z,nx,ny,nz) per vertex, 3 vertices per triangle.
    /// </summary>
    public float[] VertexData { get; }

    public int VertexCount { get; }

    public int TriangleCount { get; }

    public Bounds MeshBounds { get; }

    public double PreferredAspectRatio { get; set; }

    public Vector3 Target { get; set; }

    public float Distance { get; set; }

    public float YawRadians { get; set; }

    public float PitchRadians { get; set; }

    public float BoundsRadius => MeshBounds.Radius;

    public void ResetCameraToFit()
    {
        Target = MeshBounds.Center;
        YawRadians = 0f;
        PitchRadians = 0.35f;
        Distance = ComputeFitDistance(MeshBounds.Radius);
    }

    public BlockSize Measure(in BlockMeasureContext ctx)
    {
        var width = Math.Max(0, ctx.ContentWidthPx);
        var aspect = PreferredAspectRatio <= 0 ? (16.0 / 9.0) : PreferredAspectRatio;
        var idealHeight = (int)Math.Round(width / aspect);
        return new BlockSize(width, idealHeight);
    }

    private static float ComputeFitDistance(float radius)
    {
        radius = MathF.Max(radius, 0.0001f);
        var fov = FitFovDegrees * (MathF.PI / 180f);
        var d = radius / MathF.Tan(fov * 0.5f);
        return d * FitPaddingMultiplier;
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
