using System;
using System.Numerics;
using Cycon.Render;
using Cycon.Core.Settings;

namespace Extensions.Deconstruction.Blocks;

public sealed partial class StlBlock
{
#if DEBUG
    private bool _loggedFirstRender;
#endif

    public void Render(IRenderCanvas canvas, in BlockRenderContext ctx)
    {
        var rect = ctx.ViewportRectPx;
        var x = rect.X;
        var y = rect.Y;
        var w = rect.Width;
        var h = rect.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var settings = ctx.Scene3D;

        // Default STL rendering is double-sided unless explicitly set otherwise (debug/compat).
        var mode = (StlDebugMode)settings.StlDebugMode;
        var cullEnabled = mode == StlDebugMode.Normal;
        canvas.SetCullState(enabled: cullEnabled, frontFaceCcw: true);

        canvas.FillRect(rect, unchecked((int)0x000000FF));

#if DEBUG
        if (!_loggedFirstRender)
        {
            _loggedFirstRender = true;
            var b = MeshBounds;
            Console.WriteLine($"[STL] vertsUploaded={VertexCount} tris={TriangleCount} aabb=({b.Min.X:0.####},{b.Min.Y:0.####},{b.Min.Z:0.####})..({b.Max.X:0.####},{b.Max.Y:0.####},{b.Max.Z:0.####})");
        }
#endif

        canvas.SetDepthState(enabled: true, writeEnabled: true, DepthFunc.Less);
        canvas.SetColorWrite(true);
        canvas.ClearDepth(1f);

        var aspect = w / (float)h;
        const float fov = 60f * (MathF.PI / 180f);
        var boundsRadius = MathF.Max(MeshBounds.Radius, 0.0001f);
        var near = MathF.Max(0.01f, boundsRadius * 0.005f);
        var far = MathF.Max(near + 1f, boundsRadius * 50f);

        var forward = ComputeForward(YawRadians, PitchRadians);
        var cameraPos = Target - (forward * MathF.Max(near * 2f, Distance));
        var upWorld = Vector3.UnitY;
        var view = Matrix4x4.CreateLookAt(cameraPos, Target, upWorld);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, near, far);

        // View-space directional light (camera-aligned).
        var lightDirView = Vector3.Normalize(new Vector3(0.2f, 0.35f, 1.0f));
        canvas.DrawMesh3D(Id.Value, Mesh3DPrimitive.Triangles, VertexData, VertexCount, rect, Matrix4x4.Identity, view, proj, lightDirView, settings);
 
        if (settings.ShowStlEdges)
        {
            EnsureEdgeStatsComputed();

            // Draw a simple unlit overlay for boundary/non-manifold edges.
            // Depth test stays on so the overlay follows the solid surface; we bias slightly toward the camera to reduce z-fighting.
            canvas.SetDepthState(enabled: true, writeEnabled: false, DepthFunc.Lequal);
            const float depthBias = 0.0001f;

            var overlayMode = (StlEdgeOverlayMode)settings.StlEdgeOverlayMode;
            if ((overlayMode == StlEdgeOverlayMode.Boundary || overlayMode == StlEdgeOverlayMode.All) &&
                _boundaryEdgeVertexData is not null &&
                _boundaryEdgeVertexCount > 0)
            {
                canvas.DrawMesh3D(
                    GetBoundaryEdgeMeshId(),
                    Mesh3DPrimitive.Lines,
                    _boundaryEdgeVertexData,
                    _boundaryEdgeVertexCount,
                    rect,
                    Matrix4x4.Identity,
                    view,
                    proj,
                    lightDirView,
                    settings,
                    baseRgba: unchecked((int)0xFF4040FF),
                    depthBias: depthBias,
                    unlit: true);
            }

            if ((overlayMode == StlEdgeOverlayMode.NonManifold || overlayMode == StlEdgeOverlayMode.All) &&
                _nonManifoldEdgeVertexData is not null &&
                _nonManifoldEdgeVertexCount > 0)
            {
                canvas.DrawMesh3D(
                    GetNonManifoldEdgeMeshId(),
                    Mesh3DPrimitive.Lines,
                    _nonManifoldEdgeVertexData,
                    _nonManifoldEdgeVertexCount,
                    rect,
                    Matrix4x4.Identity,
                    view,
                    proj,
                    lightDirView,
                    settings,
                    baseRgba: unchecked((int)0xFFFF40FF),
                    depthBias: depthBias,
                    unlit: true);
            }
        }

        if (settings.VignetteStrength > 0f)
        {
            canvas.DrawVignette(rect, settings.VignetteStrength, settings.VignetteInner, settings.VignetteOuter);
        }

        canvas.SetColorWrite(true);
        canvas.SetDepthState(enabled: false, writeEnabled: false, DepthFunc.Less);
        // Restore default: no culling for 2D.
        canvas.SetCullState(enabled: false, frontFaceCcw: true);
    }

    private static Vector3 ComputeForward(float yaw, float pitch)
    {
        var cy = MathF.Cos(yaw);
        var sy = MathF.Sin(yaw);
        var cp = MathF.Cos(pitch);
        var sp = MathF.Sin(pitch);
        var forward = new Vector3(sy * cp, sp, cy * cp);
        if (forward.LengthSquared() < 1e-10f)
        {
            return new Vector3(0, 0, 1);
        }

        return Vector3.Normalize(forward);
    }
}
