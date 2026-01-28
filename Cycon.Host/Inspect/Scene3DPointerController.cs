using System;
using System.Diagnostics;
using System.Numerics;
using Cycon.Core.Settings;
using Cycon.Core.Transcript;
using Cycon.Host.Input;
using Cycon.Layout.Scrolling;

namespace Cycon.Host.Inspect;

internal sealed class Scene3DPointerController
{
    private const float MouseDeltaClampPx = 200f;

    private Scene3DCapture? _capture;

    public BlockId? CapturedBlockId => _capture?.BlockId;

    public void Reset()
    {
        _capture = null;
    }

    public bool Handle(IScene3DViewBlock stl, in PxRect viewportRectPx, in HostMouseEvent e, Scene3DSettings settings)
    {
        var insideViewport =
            e.X >= viewportRectPx.X &&
            e.Y >= viewportRectPx.Y &&
            e.X < viewportRectPx.X + viewportRectPx.Width &&
            e.Y < viewportRectPx.Y + viewportRectPx.Height;

        var nowTicks = Stopwatch.GetTimestamp();

        if (_capture is { } capture)
        {
            if (capture.BlockId != stl.Id)
            {
                return false;
            }

            switch (e.Kind)
            {
                case HostMouseEventKind.Move:
                {
                    if ((e.Buttons & capture.Button) == 0)
                    {
                        _capture = null;
                        return true;
                    }

                    var dtSeconds = (float)((nowTicks - capture.LastTicks) / (double)Stopwatch.Frequency);
                    dtSeconds = Math.Clamp(dtSeconds, 1f / 500f, 1f / 15f);

                    var isOrbitMode = stl is IScene3DOrbitBlock orbit && orbit.NavigationMode == Scene3DNavigationMode.Orbit;
                    var isFreeflyLook = !isOrbitMode && capture.Mode == Scene3DDragMode.Orbit;

                    if (isFreeflyLook)
                    {
                        var rawDx = Math.Clamp(e.X - capture.LastX, -MouseDeltaClampPx, MouseDeltaClampPx);
                        var rawDy = Math.Clamp(e.Y - capture.LastY, -MouseDeltaClampPx, MouseDeltaClampPx);

                        var tau = Math.Clamp(settings.FreeflyLookTauSeconds, 0.001f, 0.25f);
                        var alpha = 1f - MathF.Exp(-dtSeconds / tau);

                        var smoothedDx = Lerp(capture.SmoothedDx, rawDx, alpha);
                        var smoothedDy = Lerp(capture.SmoothedDy, rawDy, alpha);

                        if (MathF.Abs(smoothedDx) + MathF.Abs(smoothedDy) <= 0.05f)
                        {
                            _capture = capture with { LastX = e.X, LastY = e.Y, LastTicks = nowTicks, SmoothedDx = smoothedDx, SmoothedDy = smoothedDy };
                            return true;
                        }

                        ApplySceneDragDelta(stl, settings, capture.Mode, smoothedDx, smoothedDy);
                        _capture = capture with { LastX = e.X, LastY = e.Y, LastTicks = nowTicks, SmoothedDx = smoothedDx, SmoothedDy = smoothedDy };
                        return true;
                    }

                    {
                        var tau = Math.Clamp(settings.MouseSmoothingTauSeconds, 0.001f, 0.50f);
                        var alpha = 1f - MathF.Exp(-dtSeconds / tau);

                        var rawX = (float)e.X;
                        var rawY = (float)e.Y;
                        var smoothedX = Lerp(capture.SmoothedX, rawX, alpha);
                        var smoothedY = Lerp(capture.SmoothedY, rawY, alpha);

                        var dx = Math.Clamp(smoothedX - capture.SmoothedX, -MouseDeltaClampPx, MouseDeltaClampPx);
                        var dy = Math.Clamp(smoothedY - capture.SmoothedY, -MouseDeltaClampPx, MouseDeltaClampPx);

                        if (MathF.Abs(dx) + MathF.Abs(dy) <= 0.05f)
                        {
                            _capture = capture with { LastX = e.X, LastY = e.Y, LastTicks = nowTicks, SmoothedX = smoothedX, SmoothedY = smoothedY };
                            return true;
                        }

                        ApplySceneDragDelta(stl, settings, capture.Mode, dx, dy);
                        _capture = capture with { LastX = e.X, LastY = e.Y, LastTicks = nowTicks, SmoothedX = smoothedX, SmoothedY = smoothedY };
                    }
                    return true;
                }
                case HostMouseEventKind.Up when (e.Buttons & capture.Button) != 0:
                    _capture = null;
                    return true;
                case HostMouseEventKind.Wheel:
                    return true;
            }

            return true;
        }

        if (!insideViewport)
        {
            if (e.Kind == HostMouseEventKind.Down)
            {
                if (stl is IMouseFocusableViewportBlock focusable)
                {
                    focusable.HasMouseFocus = false;
                }
            }

            return false;
        }

        if (e.Kind == HostMouseEventKind.Wheel)
        {
            ApplySceneZoom(stl, settings, e.WheelDelta);
            return true;
        }

        if (e.Kind == HostMouseEventKind.Move)
        {
            return false;
        }

        if (e.Kind == HostMouseEventKind.Down)
        {
            if ((e.Buttons & HostMouseButtons.Left) != 0)
            {
                if (stl is IMouseFocusableViewportBlock focusable)
                {
                    focusable.HasMouseFocus = true;
                }
                _capture = new Scene3DCapture(stl.Id, HostMouseButtons.Left, Scene3DDragMode.Orbit, e.X, e.Y, nowTicks, e.X, e.Y, 0f, 0f);
                return true;
            }

            if ((e.Buttons & HostMouseButtons.Right) != 0)
            {
                _capture = new Scene3DCapture(stl.Id, HostMouseButtons.Right, Scene3DDragMode.Pan, e.X, e.Y, nowTicks, e.X, e.Y, 0f, 0f);
                return true;
            }
        }

        return false;
    }

    private static void ApplySceneDragDelta(IScene3DViewBlock stl, Scene3DSettings settings, Scene3DDragMode mode, float dx, float dy)
    {
        if (MathF.Abs(dx) + MathF.Abs(dy) <= 1f)
        {
            return;
        }

        if (stl is IScene3DOrbitBlock orbit && orbit.NavigationMode == Scene3DNavigationMode.Orbit)
        {
            var target = orbit.OrbitTarget;
            var distance = Math.Max(0.01f, orbit.OrbitDistance);

            var yaw = orbit.OrbitYaw;
            var pitch = orbit.OrbitPitch;

            switch (mode)
            {
                case Scene3DDragMode.Orbit:
                {
                    yaw += dx * settings.OrbitSensitivity * (settings.InvertOrbitX ? -1f : 1f);
                    pitch -= dy * settings.OrbitSensitivity * (settings.InvertOrbitY ? -1f : 1f);
                    pitch = Math.Clamp(pitch, -1.55f, 1.55f);
                    break;
                }
                case Scene3DDragMode.Pan:
                {
                    var forward = ComputeForward(yaw, pitch);
                    var (right, up, _) = ComputeSceneBasis(forward);
                    var scale = Math.Max(0.01f, distance) * settings.PanSensitivity;
                    var delta = (-dx * scale * (settings.InvertPanX ? -1f : 1f)) * right +
                                (dy * scale * (settings.InvertPanY ? -1f : 1f)) * up;
                    target += delta;
                    break;
                }
            }

            orbit.OrbitYaw = yaw;
            orbit.OrbitPitch = pitch;
            orbit.OrbitTarget = target;
            orbit.OrbitDistance = distance;

            var dir = ComputeForward(yaw, pitch);
            stl.CenterDir = dir;
            stl.FocusDistance = distance;
            stl.CameraPos = target - (dir * distance);
            return;
        }

        switch (mode)
        {
            case Scene3DDragMode.Orbit:
            {
                var yaw = MathF.Atan2(stl.CenterDir.X, stl.CenterDir.Z);
                var pitch = MathF.Asin(Math.Clamp(stl.CenterDir.Y, -1f, 1f));
                var lookSensitivity = settings.FreeflyLookSensitivity;
                yaw += dx * lookSensitivity * (settings.InvertOrbitX ? -1f : 1f);
                pitch -= dy * lookSensitivity * (settings.InvertOrbitY ? -1f : 1f);
                pitch = Math.Clamp(pitch, -1.55f, 1.55f);
                stl.CenterDir = ComputeForward(yaw, pitch);
                break;
            }
            case Scene3DDragMode.Pan:
            {
                var (right, up, _) = ComputeSceneBasis(stl.CenterDir);
                var scale = Math.Max(0.01f, stl.FocusDistance) * settings.PanSensitivity;
                stl.CameraPos += (-dx * scale * (settings.InvertPanX ? -1f : 1f)) * right;
                stl.CameraPos += (dy * scale * (settings.InvertPanY ? -1f : 1f)) * up;
                break;
            }
        }
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static void ApplySceneZoom(IScene3DViewBlock stl, Scene3DSettings settings, int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return;
        }

        // Wheel deltas are pixel-scaled (48px per wheel unit) to support smooth scrolling.
        // Normalize back to wheel "units" for camera zoom.
        var deltaUnits = (settings.InvertZoom ? -wheelDelta : wheelDelta) / 48f;
        var factor = MathF.Exp(-deltaUnits * settings.ZoomSensitivity);
        ApplySceneDollyFactor(stl, factor);
    }

    internal static void ApplySceneDollyFactor(IScene3DViewBlock stl, float factor)
    {
        if (stl is IScene3DOrbitBlock orbit && orbit.NavigationMode == Scene3DNavigationMode.Orbit)
        {
            var oldDistance = Math.Max(0.01f, orbit.OrbitDistance);
            var maxDistance = MathF.Max(0.01f, stl.BoundsRadius * 100f);
            var newDistance = Math.Clamp(oldDistance * factor, 0.01f, maxDistance);
            if (MathF.Abs(newDistance - oldDistance) < 1e-6f)
            {
                return;
            }

            orbit.OrbitDistance = newDistance;

            var dir = stl.CenterDir;
            if (dir.LengthSquared() < 1e-10f)
            {
                dir = new Vector3(0, 0, 1);
            }
            else
            {
                dir = Vector3.Normalize(dir);
            }

            stl.CenterDir = dir;
            stl.FocusDistance = newDistance;
            stl.CameraPos = orbit.OrbitTarget - (dir * newDistance);
            return;
        }

        var oldFocus = Math.Max(0.01f, stl.FocusDistance);
        var maxFocus = MathF.Max(0.01f, stl.BoundsRadius * 100f);
        var newFocus = Math.Clamp(oldFocus * factor, 0.01f, maxFocus);
        if (MathF.Abs(newFocus - oldFocus) < 1e-6f)
        {
            return;
        }

        var forward = stl.CenterDir;
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }
        else
        {
            forward = Vector3.Normalize(forward);
        }

        stl.CenterDir = forward;
        stl.CameraPos += forward * (oldFocus - newFocus);
        stl.FocusDistance = newFocus;
    }

    internal static (Vector3 Right, Vector3 Up, Vector3 Forward) ComputeSceneBasis(Vector3 forward)
    {
        if (forward.LengthSquared() < 1e-10f)
        {
            forward = new Vector3(0, 0, 1);
        }

        forward = Vector3.Normalize(forward);

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

        return (right, up, forward);
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

    private enum Scene3DDragMode
    {
        Orbit,
        Pan
    }

    private readonly record struct Scene3DCapture(
        BlockId BlockId,
        HostMouseButtons Button,
        Scene3DDragMode Mode,
        int LastX,
        int LastY,
        long LastTicks,
        float SmoothedX,
        float SmoothedY,
        float SmoothedDx,
        float SmoothedDy);
}
