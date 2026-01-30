using System.Numerics;
using System.Collections.Generic;
using Cycon.Render;

namespace Cycon.Rendering.Commands;

public abstract record DrawCommand;

public sealed record DrawGlyphRun(int X, int Y, IReadOnlyList<GlyphInstance> Glyphs) : DrawCommand;

public sealed record DrawQuad(int X, int Y, int Width, int Height, int Rgba) : DrawCommand;

public sealed record DrawTriangles(IReadOnlyList<SolidVertex> Vertices) : DrawCommand;

public sealed record ReleaseMesh3D(int MeshId) : DrawCommand;

// Draw a previously uploaded mesh into a per-command viewport rect.
public sealed record DrawMesh3D(
    int MeshId,
    float[] VertexData,
    int VertexCount,
    RectPx ViewportRectPx,
    Matrix4x4 Model,
    Matrix4x4 View,
    Matrix4x4 Proj,
    Vector3 LightDirView,
    Scene3DRenderSettings Settings) : DrawCommand;

public sealed record DrawVignetteQuad(int X, int Y, int Width, int Height, float Strength, float Inner, float Outer) : DrawCommand;

public sealed record DrawImage2D(
    int ImageId,
    byte[] RgbaPixels,
    int Width,
    int Height,
    RectF DestRectPx,
    bool UseNearest) : DrawCommand;

public sealed record ReleaseImage2D(int ImageId) : DrawCommand;

// Modal backdrop blur: capture the background into an offscreen texture, blur it, then present it.
// Intended for modal overlays (slabs) only; overlay content should be drawn after the blur is presented.
public sealed record BeginModalBackdropBlur(long CacheKey) : DrawCommand;

public sealed record EndModalBackdropBlur : DrawCommand;

public sealed record DrawModalBackdropBlur(long CacheKey) : DrawCommand;

// Debug/profiling metadata for renderers/executors. Not intended for UI logic.
public sealed record SetDebugTag(int Tag) : DrawCommand;

// Set culling state for subsequent draws until changed again.
public sealed record SetCullState(bool Enabled, bool FrontFaceCcw) : DrawCommand;

public sealed record SetColorWrite(bool Enabled) : DrawCommand;

public sealed record SetDepthState(bool Enabled, bool WriteEnabled, DepthFuncKind Func) : DrawCommand;

public sealed record ClearDepth(float Depth01 = 1f) : DrawCommand;

public sealed record PushClip(int X, int Y, int Width, int Height) : DrawCommand;

public sealed record PopClip() : DrawCommand;

public readonly record struct GlyphInstance(int Codepoint, int X, int Y, int Rgba);

public readonly record struct SolidVertex(float X, float Y, int Rgba);

public enum DepthFuncKind
{
    Less,
    Lequal,
    Greater,
    Always
}
