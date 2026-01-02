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
                var lookAt = CameraPos + (CenterDir * FocusDistance);
                var scale = MathF.Tan(oldVfov * 0.5f) / MathF.Tan(vfov * 0.5f);
                FocusDistance *= scale;
                CameraPos = lookAt - (CenterDir * FocusDistance);
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
                $"[STL-PROJ] vp={w}x{h} aspect={aspect:0.####} hfovDeg={_lastHorizontalFovDegrees:0.####} vfovDeg={(_lastVerticalFovRadians * (180f / MathF.PI)):0.####} focus={FocusDistance:0.####} near={near:0.####} far={far:0.####}");
        }
#endif

        var forward = CenterDir;
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }

        forward = Vector3.Normalize(forward);
        CenterDir = forward;

        var lookAtPoint = CameraPos + (forward * MathF.Max(near * 2f, FocusDistance));

        var worldUp = Vector3.UnitY;
        if (MathF.Abs(Vector3.Dot(forward, worldUp)) > 0.99f)
        {
            worldUp = Vector3.UnitZ;
        }

        // Use a stable right-handed basis: right = up × forward, up = forward × right.
        var right = Vector3.Cross(worldUp, forward);
        if (right.LengthSquared() < 1e-10f)
        {
            right = Vector3.UnitX;
        }
        else
        {
            right = Vector3.Normalize(right);
        }

        var up = Vector3.Cross(forward, right);
        if (up.LengthSquared() < 1e-10f)
        {
            up = Vector3.UnitY;
        }
        else
        {
            up = Vector3.Normalize(up);
        }

        var view = Matrix4x4.CreateLookAt(CameraPos, lookAtPoint, up);
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

    // Center-ray camera model: view forward is tracked by CenterDir in the block.
}
