using System;
using System.Numerics;
using Cycon.Render;
using Cycon.Core.Settings;

namespace Extensions.Inspect.Blocks;

public sealed partial class StlBlock
{
#if DEBUG
    private bool _loggedFirstRender;
    private int _loggedViewportW;
    private int _loggedViewportH;
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
        _lastProjectionAspect = aspect;
        _lastHorizontalFovDegrees = settings.HorizontalFovDegrees > 0 ? settings.HorizontalFovDegrees : 80f;
        var vfov = ComputeVerticalFovRadians(_lastHorizontalFovDegrees, aspect);

        if (float.IsFinite(_lastVerticalFovRadians))
        {
            var oldVfov = _lastVerticalFovRadians;
            if (MathF.Abs(vfov - oldVfov) > 1e-6f)
            {
                var scale = MathF.Tan(oldVfov * 0.5f) / MathF.Tan(vfov * 0.5f);
                Distance *= scale;
                _lastVerticalFovRadians = vfov;
            }
        }
        else
        {
            _lastVerticalFovRadians = vfov;
        }

        var boundsRadius = MathF.Max(MeshBounds.Radius, 0.0001f);
        var near = MathF.Max(0.01f, boundsRadius * 0.005f);
        var far = MathF.Max(near + 1f, boundsRadius * 50f);

#if DEBUG
        if (_loggedViewportW != w || _loggedViewportH != h)
        {
            _loggedViewportW = w;
            _loggedViewportH = h;
            Console.WriteLine(
                $"[STL-PROJ] vp={w}x{h} aspect={aspect:0.####} hfovDeg={_lastHorizontalFovDegrees:0.####} vfovDeg={(_lastVerticalFovRadians * (180f / MathF.PI)):0.####} dist={Distance:0.####} near={near:0.####} far={far:0.####}");
        }
#endif

        var forward = ComputeForward(YawRadians, PitchRadians);
        var cameraPos = Target - (forward * MathF.Max(near * 2f, Distance));
        var upWorld = Vector3.UnitY;
        var view = Matrix4x4.CreateLookAt(cameraPos, Target, upWorld);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(vfov, aspect, near, far);

        // View-space directional light (camera-aligned).
        var lightDirView = Vector3.Normalize(new Vector3(0.2f, 0.35f, 1.0f));
        canvas.DrawMesh3D(Id.Value, VertexData, VertexCount, rect, Matrix4x4.Identity, view, proj, lightDirView, settings);

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
