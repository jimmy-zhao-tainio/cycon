using System;
using System.Collections.Generic;
using System.Numerics;
using Cycon.Render;

namespace Extensions.Deconstruction.Blocks;

public sealed partial class StlBlock
{
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

        canvas.FillRect(rect, unchecked((int)0x000000FF));

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
        var viewProj = view * proj;

        var lightDir = Vector3.Normalize(-forward);

        if (Indices.Length >= 3)
        {
            RenderSolidShaded(canvas, settings, Vertices, Indices, viewProj, lightDir, x, y, w, h);
        }

        if (settings.VignetteStrength > 0f)
        {
            canvas.DrawVignette(rect, settings.VignetteStrength, settings.VignetteInner, settings.VignetteOuter);
        }

        canvas.SetColorWrite(true);
        canvas.SetDepthState(enabled: false, writeEnabled: false, DepthFunc.Less);
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

    private readonly record struct Projected(Vector2 Screen, float Depth01);

    private static bool TryProject(Matrix4x4 viewProj, Vector3 world, int vx, int vy, int vw, int vh, out Projected screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (MathF.Abs(clip.W) < 1e-8f)
        {
            screen = default;
            return false;
        }

        var invW = 1f / clip.W;
        var ndcX = clip.X * invW;
        var ndcY = clip.Y * invW;
        var ndcZ = clip.Z * invW;

        if (ndcX is < -1.5f or > 1.5f || ndcY is < -1.5f or > 1.5f || ndcZ is < -1.5f or > 1.5f)
        {
            screen = default;
            return false;
        }

        var sx = vx + ((ndcX + 1f) * 0.5f * vw);
        var sy = vy + ((1f - (ndcY + 1f) * 0.5f) * vh);
        var depth01 = (ndcZ + 1f) * 0.5f;
        screen = new Projected(new Vector2(sx, sy), depth01);
        return true;
    }

    private static void RenderSolidShaded(
        IRenderCanvas canvas,
        Scene3DRenderSettings settings,
        Vector3[] vertices,
        int[] indices,
        Matrix4x4 viewProj,
        Vector3 lightDir,
        int x,
        int y,
        int w,
        int h)
    {
        canvas.SetDepthState(enabled: true, writeEnabled: true, DepthFunc.Less);
        canvas.SetColorWrite(true);
        canvas.ClearDepth(1f);

        var tris = new List<SolidVertex3D>(Math.Min(indices.Length, 600_000));
        var triLimit = Math.Min(indices.Length / 3, 75_000);
        for (var tri = 0; tri < triLimit; tri++)
        {
            var t = tri * 3;
            var i0 = indices[t];
            var i1 = indices[t + 1];
            var i2 = indices[t + 2];
            if ((uint)i0 >= (uint)vertices.Length || (uint)i1 >= (uint)vertices.Length || (uint)i2 >= (uint)vertices.Length)
            {
                continue;
            }

            var v0 = vertices[i0];
            var v1 = vertices[i1];
            var v2 = vertices[i2];

            var n = Vector3.Cross(v1 - v0, v2 - v0);
            if (n.LengthSquared() < 1e-10f)
            {
                continue;
            }

            n = Vector3.Normalize(n);
            var ndotl = MathF.Abs(Vector3.Dot(n, lightDir));
            var b = settings.SolidAmbient + (settings.SolidDiffuseStrength * ndotl);
            b = ApplyTone(b, settings.ToneGamma, settings.ToneGain, settings.ToneLift);
            var rgba = PackGrayscale(b);

            if (!TryProject(viewProj, v0, x, y, w, h, out var p0) ||
                !TryProject(viewProj, v1, x, y, w, h, out var p1) ||
                !TryProject(viewProj, v2, x, y, w, h, out var p2))
            {
                continue;
            }

            tris.Add(new SolidVertex3D(p0.Screen.X, p0.Screen.Y, p0.Depth01, rgba));
            tris.Add(new SolidVertex3D(p1.Screen.X, p1.Screen.Y, p1.Depth01, rgba));
            tris.Add(new SolidVertex3D(p2.Screen.X, p2.Screen.Y, p2.Depth01, rgba));
        }

        if (tris.Count > 0)
        {
            canvas.DrawTriangles3D(tris);
        }
    }

    private static float ApplyTone(float brightness, float gamma, float gain, float lift)
    {
        brightness = Math.Clamp(brightness, 0f, 1f);
        gamma = gamma <= 0f ? 1f : gamma;
        brightness = MathF.Pow(brightness, gamma);
        brightness = (brightness * gain) + lift;
        return Math.Clamp(brightness, 0f, 1f);
    }

    private static int PackGrayscale(float brightness)
    {
        var v = (byte)Math.Clamp((int)Math.Round(brightness * 255f), 0, 255);
        return (v << 24) | (v << 16) | (v << 8) | 0xFF;
    }

    
}
