using System.Collections.Generic;
using Cycon.Render;

namespace Cycon.Backends.Abstractions.Rendering;

public abstract record DrawCommand;

public sealed record DrawGlyphRun(int X, int Y, IReadOnlyList<GlyphInstance> Glyphs) : DrawCommand;

public sealed record DrawQuad(int X, int Y, int Width, int Height, int Rgba) : DrawCommand;

public sealed record DrawTriangles(IReadOnlyList<SolidVertex> Vertices) : DrawCommand;

public sealed record DrawTriangles3D(IReadOnlyList<SolidVertex3D> Vertices) : DrawCommand;

public sealed record DrawVignetteQuad(int X, int Y, int Width, int Height, float Strength, float Inner, float Outer) : DrawCommand;

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
