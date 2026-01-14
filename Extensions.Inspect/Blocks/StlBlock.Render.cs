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
        }
#endif

        canvas.SetDepthState(enabled: true, writeEnabled: true, DepthFunc.Less);
        canvas.SetColorWrite(true);
        canvas.ClearDepth(1f);

        var viewRect = rect;

        var aspect = viewRect.Width / (float)viewRect.Height;
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

                if (NavigationMode == Cycon.Core.Transcript.Scene3DNavigationMode.Orbit)
                {
                    SyncOrbitFromCurrentView();
                }
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
        }
#endif

        var forward = CenterDir;
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }

        forward = Vector3.Normalize(forward);
        CenterDir = forward;

        if (NavigationMode == Cycon.Core.Transcript.Scene3DNavigationMode.Orbit)
        {
            SyncOrbitFromCurrentView();
        }

        var lookAtPoint = CameraPos + (forward * MathF.Max(near * 2f, FocusDistance));

        var (_, up) = ComputeStableBasis(forward);

        // Use a stable right-handed basis: right = up × forward, up = forward × right.
        var view = Matrix4x4.CreateLookAt(CameraPos, lookAtPoint, up);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(vfov, aspect, near, far);

        // View-space directional light (camera-aligned).
        var lightDirView = Vector3.Normalize(new Vector3(0.2f, 0.35f, 1.0f));
        canvas.DrawMesh3D(Id.Value, VertexData, VertexCount, viewRect, Matrix4x4.Identity, view, proj, lightDirView, settings);

        if (settings.VignetteStrength > 0f)
        {
            canvas.DrawVignette(viewRect, settings.VignetteStrength, settings.VignetteInner, settings.VignetteOuter);
        }

        canvas.SetColorWrite(true);
        canvas.SetDepthState(enabled: false, writeEnabled: false, DepthFunc.Less);
        // Restore default: no culling for 2D.
        canvas.SetCullState(enabled: false, frontFaceCcw: true);
    }

    // Center-ray camera model: view forward is tracked by CenterDir in the block.

    private static (Vector3 Right, Vector3 Up) ComputeStableBasis(Vector3 forward)
    {
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }

        forward = Vector3.Normalize(forward);

        // Project a reference "up" onto the plane perpendicular to forward.
        // This avoids discontinuous axis switching when near the poles.
        var upRef = Vector3.UnitY;
        var up = upRef - (forward * Vector3.Dot(upRef, forward));
        if (up.LengthSquared() < 1e-8f)
        {
            upRef = Vector3.UnitZ;
            up = upRef - (forward * Vector3.Dot(upRef, forward));
            if (up.LengthSquared() < 1e-8f)
            {
                upRef = Vector3.UnitX;
                up = upRef - (forward * Vector3.Dot(upRef, forward));
            }
        }

        up = up.LengthSquared() < 1e-10f ? Vector3.UnitY : Vector3.Normalize(up);

        var right = Vector3.Cross(up, forward);
        right = right.LengthSquared() < 1e-10f ? Vector3.UnitX : Vector3.Normalize(right);

        up = Vector3.Cross(forward, right);
        up = up.LengthSquared() < 1e-10f ? Vector3.UnitY : Vector3.Normalize(up);

        return (right, up);
    }
}
