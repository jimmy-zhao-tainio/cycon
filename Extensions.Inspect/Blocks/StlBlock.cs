using System;
using System.IO;
using System.Numerics;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Render;

namespace Extensions.Inspect.Blocks;

public sealed partial class StlBlock : IScene3DViewBlock, IScene3DOrbitBlock, IMouseFocusableViewportBlock, IRenderBlock, IMeasureBlock, IMesh3DResourceOwner, IBlockChromeProvider, IInspectChromeProvider
{
    private const float DefaultHorizontalFovDegrees = 80f;
    private const float FitPaddingMultiplier = 0.75f;

    private float _lastProjectionAspect = float.NaN;
    private float _lastHorizontalFovDegrees = DefaultHorizontalFovDegrees;
    private float _lastVerticalFovRadians = float.NaN;
    private int _initialHeightRows = -1;
    private int _fixedHeightPx = -1;

    private static readonly InspectPanelSpec[] InspectPanels =
        new[]
        {
            new InspectPanelSpec(InspectEdge.Top, SizeCells: 2, DrawSeparator: true),
            new InspectPanelSpec(InspectEdge.Bottom, SizeCells: 2, DrawSeparator: true),
            new InspectPanelSpec(InspectEdge.Left, SizeCells: 24, DrawSeparator: true)
        };

    private static readonly InspectTextRowSpec[] InspectTextRows =
        new[]
        {
            new InspectTextRowSpec(InspectEdge.Top, RowIndex: 0, LeftKey: "stl.mode", CenterKey: null, RightKey: "stl.source"),
            new InspectTextRowSpec(InspectEdge.Bottom, RowIndex: 1, LeftKey: "stl.stats", CenterKey: "stl.selection", RightKey: "stl.quant"),

            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 0, LeftKey: "stl.diag.0", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 1, LeftKey: "stl.diag.1", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 2, LeftKey: "stl.diag.2", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 3, LeftKey: "stl.diag.3", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 4, LeftKey: "stl.diag.4", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 5, LeftKey: "stl.diag.5", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 6, LeftKey: "stl.diag.6", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 7, LeftKey: "stl.diag.7", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 8, LeftKey: "stl.diag.8", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 9, LeftKey: "stl.diag.9", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 10, LeftKey: "stl.diag.10", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 11, LeftKey: "stl.diag.11", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 12, LeftKey: "stl.diag.12", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 13, LeftKey: "stl.diag.13", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 14, LeftKey: "stl.diag.14", CenterKey: null, RightKey: null),
            new InspectTextRowSpec(InspectEdge.Left, RowIndex: 15, LeftKey: "stl.diag.15", CenterKey: null, RightKey: null)
        };

    public Scene3DNavigationMode NavigationMode { get; set; } = Scene3DNavigationMode.Orbit;
    public Vector3 OrbitTarget { get; set; }
    public float OrbitDistance { get; set; }
    public float OrbitYaw { get; set; }
    public float OrbitPitch { get; set; }

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

        SyncOrbitFromRay();
    }

    public BlockId Id { get; }

    public BlockKind Kind => BlockKind.Scene3D;

    public int MeshId => Id.Value;

    public bool HasMouseFocus { get; set; }

    public string FilePath { get; }

    public BlockChromeSpec ChromeSpec => BlockChromeSpec.ViewDefault;

    public InspectChromeSpec GetInspectChromeSpec() =>
        new(Enabled: true, StyleId: InspectChromeStyleId.Frame2Px, OuterBorderPx: 0, Panels: InspectPanels, TextRows: InspectTextRows);

    public void PopulateInspectChromeData(ref InspectChromeDataBuilder b)
    {
        var mode = NavigationMode == Scene3DNavigationMode.Orbit ? "ORB" : "FPS";
        b.Set("stl.mode", $"STL  {mode}");
        b.Set("stl.source", $"SRC {Path.GetFileName(FilePath)}");
        b.Set("stl.stats", $"Tri {TriangleCount}  Vtx {VertexCount}");
        b.Set("stl.selection", "Sel none");
        b.Set("stl.quant", "Q 1e-05");

        b.Set("stl.diag.0", "Geometry");
        b.Set("stl.diag.1", $"  Tri  {TriangleCount}");
        b.Set("stl.diag.2", $"  Vtx  {VertexCount}");
        b.Set("stl.diag.3", string.Empty);
        b.Set("stl.diag.4", "Bounds");
        b.Set("stl.diag.5", "  Min  +000 +000 +000");
        b.Set("stl.diag.6", "  Max  +000 +000 +000");
        b.Set("stl.diag.7", string.Empty);
        b.Set("stl.diag.8", "Camera");
        b.Set("stl.diag.9", "  Mode ORB");
        b.Set("stl.diag.10", "  FOV  45.0");
        b.Set("stl.diag.11", "  Dist +0.000");
        b.Set("stl.diag.12", string.Empty);
        b.Set("stl.diag.13", "Selection");
        b.Set("stl.diag.14", "  Kind none");
        b.Set("stl.diag.15", "  Id   ------");
    }

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
        SyncOrbitFromRay();
    }

    public BlockSize Measure(in BlockMeasureContext ctx)
    {
        var width = Math.Max(0, ctx.ContentWidthPx);
        var cellH = Math.Max(1, ctx.CellHeightPx);
        var viewportRows = Math.Max(1, ctx.ViewportRows);
        var promptReservedRows = 2;
        var availableRows = Math.Max(1, viewportRows - promptReservedRows);
        if (_initialHeightRows < 0)
        {
            _initialHeightRows = availableRows;
        }

        if (_fixedHeightPx < 0)
        {
            var heightRows = Math.Min(availableRows, _initialHeightRows);
            _fixedHeightPx = checked(heightRows * cellH);
        }

        return new BlockSize(width, _fixedHeightPx);
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

    private void SyncOrbitFromRay()
    {
        OrbitTarget = MeshBounds.Center;
        OrbitDistance = Math.Max(0.01f, FocusDistance);

        var forward = CenterDir;
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }
        else
        {
            forward = Vector3.Normalize(forward);
        }

        OrbitYaw = MathF.Atan2(forward.X, forward.Z);
        OrbitPitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
    }

    private void SyncOrbitFromCurrentView()
    {
        OrbitDistance = Math.Max(0.01f, FocusDistance);

        var forward = CenterDir;
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }
        else
        {
            forward = Vector3.Normalize(forward);
        }

        CenterDir = forward;
        OrbitTarget = CameraPos + (forward * OrbitDistance);
        OrbitYaw = MathF.Atan2(forward.X, forward.Z);
        OrbitPitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
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
