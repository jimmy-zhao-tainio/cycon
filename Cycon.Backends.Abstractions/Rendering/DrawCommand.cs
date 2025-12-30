using System.Collections.Generic;

namespace Cycon.Backends.Abstractions.Rendering;

public abstract record DrawCommand;

public sealed record DrawGlyphRun(int X, int Y, IReadOnlyList<GlyphInstance> Glyphs) : DrawCommand;

public sealed record DrawQuad(int X, int Y, int Width, int Height, int Rgba) : DrawCommand;

public sealed record PushClip(int X, int Y, int Width, int Height) : DrawCommand;

public sealed record PopClip() : DrawCommand;

public readonly record struct GlyphInstance(int Codepoint, int X, int Y, int Rgba);
