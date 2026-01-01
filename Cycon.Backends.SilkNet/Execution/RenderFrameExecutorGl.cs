using System;
using System.Collections.Generic;
using Cycon.Backends.Abstractions.Rendering;
using Cycon.Backends.SilkNet.Execution.Shaders;
using Silk.NET.OpenGL;

namespace Cycon.Backends.SilkNet.Execution;

public sealed class RenderFrameExecutorGl : IDisposable
{
    private const int FloatsPerVertex = 8;

    private readonly GL _gl;
    private uint _glyphProgram;
    private uint _quadProgram;
    private uint _tri3dProgram;
    private uint _vignetteProgram;
    private uint _vao;
    private uint _vbo;
    private uint _atlasTexture;
    private int _viewportWidth;
    private int _viewportHeight;
    private int _uViewportLocationGlyph;
    private int _uAtlasLocationGlyph;
    private int _uViewportLocationQuad;
    private int _uViewportLocationTri3d;
    private int _uViewportLocationVignette;
    private int _uRectLocationVignette;
    private int _uParamsLocationVignette;
    private bool _initialized;
    private bool _disposed;
    private readonly bool _trace = Environment.GetEnvironmentVariable("CYCON_GL_TRACE") == "1";
    private readonly Stack<(int X, int Y, int Width, int Height)> _clipStack = new();

    public RenderFrameExecutorGl(GL gl)
    {
        _gl = gl;
    }

    public void Initialize(GlyphAtlasData atlas)
    {
        if (_initialized)
        {
            return;
        }

        _glyphProgram = CreateProgram(ShaderSources.Vertex, ShaderSources.FragmentGlyph);
        _uViewportLocationGlyph = _gl.GetUniformLocation(_glyphProgram, "uViewport");
        _uAtlasLocationGlyph = _gl.GetUniformLocation(_glyphProgram, "uAtlas");

        _quadProgram = CreateProgram(ShaderSources.Vertex, ShaderSources.FragmentQuad);
        _uViewportLocationQuad = _gl.GetUniformLocation(_quadProgram, "uViewport");

        _tri3dProgram = CreateProgram(ShaderSources.Vertex3D, ShaderSources.FragmentQuad);
        _uViewportLocationTri3d = _gl.GetUniformLocation(_tri3dProgram, "uViewport");

        _vignetteProgram = CreateProgram(ShaderSources.Vertex, ShaderSources.FragmentVignette);
        _uViewportLocationVignette = _gl.GetUniformLocation(_vignetteProgram, "uViewport");
        _uRectLocationVignette = _gl.GetUniformLocation(_vignetteProgram, "uRect");
        _uParamsLocationVignette = _gl.GetUniformLocation(_vignetteProgram, "uParams");

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        var stride = (uint)(FloatsPerVertex * sizeof(float));
        unsafe
        {
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
        }

        _atlasTexture = CreateAtlasTexture(atlas);

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Multisample);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

#if DEBUG
        if (_trace)
        {
            var sampleBuffers = _gl.GetInteger(GLEnum.SampleBuffers);
            var samples = _gl.GetInteger(GLEnum.Samples);
            Console.WriteLine($"[GL] sampleBuffers={sampleBuffers} samples={samples}");
        }
#endif

        _initialized = true;
    }

    public void Resize(int framebufferWidth, int framebufferHeight)
    {
        _viewportWidth = framebufferWidth;
        _viewportHeight = framebufferHeight;
        _gl.Viewport(0, 0, (uint)framebufferWidth, (uint)framebufferHeight);

        if (_initialized)
        {
            _gl.UseProgram(_glyphProgram);
            _gl.Uniform2(_uViewportLocationGlyph, (float)_viewportWidth, (float)_viewportHeight);

            _gl.UseProgram(_quadProgram);
            _gl.Uniform2(_uViewportLocationQuad, (float)_viewportWidth, (float)_viewportHeight);

            _gl.UseProgram(_tri3dProgram);
            _gl.Uniform2(_uViewportLocationTri3d, (float)_viewportWidth, (float)_viewportHeight);

            _gl.UseProgram(_vignetteProgram);
            _gl.Uniform2(_uViewportLocationVignette, (float)_viewportWidth, (float)_viewportHeight);
        }
    }

    public void Execute(RenderFrame frame, GlyphAtlasData atlas)
    {
        if (!_initialized)
        {
            Initialize(atlas);
        }

        if (_viewportWidth <= 0 || _viewportHeight <= 0)
        {
            return;
        }

        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        ExecuteBatched(frame, atlas);
    }

    public void ClearOnly()
    {
        if (!_initialized)
        {
            return;
        }

        if (_viewportWidth <= 0 || _viewportHeight <= 0)
        {
            return;
        }

        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    private List<float> BuildVertices(RenderFrame frame, GlyphAtlasData atlas)
    {
        var vertices = new List<float>();
        foreach (var command in frame.Commands)
        {
            switch (command)
            {
                case DrawGlyphRun glyphRun:
                    AddGlyphRunVertices(vertices, glyphRun, atlas);
                    break;
                case DrawQuad quad:
                    AddQuadVertices(vertices, quad);
                    break;
            }
        }

        return vertices;
    }

    private enum DrawBatchKind
    {
        Glyph,
        Quad,
        Tri3D
    }

    private void ExecuteBatched(RenderFrame frame, GlyphAtlasData atlas)
    {
        _clipStack.Clear();
        ApplyClipState();

        var commands = frame.Commands;
        var vertices = new List<float>();
        DrawBatchKind? currentKind = null;

        void Flush()
        {
            if (currentKind is null || vertices.Count == 0)
            {
                vertices.Clear();
                return;
            }

            DrawVertices(currentKind.Value, vertices, atlas);
            vertices.Clear();
        }

        for (var i = 0; i < commands.Count; i++)
        {
            switch (commands[i])
            {
                case SetColorWrite cw:
                    Flush();
                    SetColorWrite(cw.Enabled);
                    break;
                case SetDepthState depth:
                    Flush();
                    SetDepthState(depth.Enabled, depth.WriteEnabled, depth.Func);
                    break;
                case ClearDepth clearDepth:
                    Flush();
                    ClearDepth(clearDepth.Depth01);
                    break;
                case PushClip clip:
                    Flush();
                    PushClipRect(clip.X, clip.Y, clip.Width, clip.Height);
                    break;
                case Cycon.Backends.Abstractions.Rendering.PopClip:
                    Flush();
                    PopClipRect();
                    break;
                case DrawGlyphRun glyphRun:
                    if (currentKind != DrawBatchKind.Glyph)
                    {
                        Flush();
                        currentKind = DrawBatchKind.Glyph;
                        TraceSwitch(currentKind.Value);
                    }

                    AddGlyphRunVertices(vertices, glyphRun, atlas);
                    break;
                case DrawQuad quad:
                    if (currentKind != DrawBatchKind.Quad)
                    {
                        Flush();
                        currentKind = DrawBatchKind.Quad;
                        TraceSwitch(currentKind.Value);
                    }

                    AddQuadVertices(vertices, quad);
                    break;
                case DrawTriangles tris:
                    if (currentKind != DrawBatchKind.Quad)
                    {
                        Flush();
                        currentKind = DrawBatchKind.Quad;
                        TraceSwitch(currentKind.Value);
                    }

                    AddTriangleVertices(vertices, tris);
                    break;
                case DrawTriangles3D tris3d:
                    if (currentKind != DrawBatchKind.Tri3D)
                    {
                        Flush();
                        currentKind = DrawBatchKind.Tri3D;
                        TraceSwitch(currentKind.Value);
                    }

                    AddTriangle3DVertices(vertices, tris3d);
                    break;
                case DrawVignetteQuad vignette:
                    Flush();
                    DrawVignette(vignette);
                    currentKind = null;
                    break;
            }
        }

        Flush();
    }

    private void TraceSwitch(DrawBatchKind kind)
    {
        if (_trace)
        {
            Console.WriteLine($"[CYCON_GL_TRACE] switch -> {kind}");
        }
    }

    private void PushClipRect(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0)
        {
            _clipStack.Push((0, 0, 0, 0));
            ApplyClipState();
            return;
        }

        if (_clipStack.Count == 0)
        {
            _clipStack.Push((x, y, w, h));
            ApplyClipState();
            return;
        }

        var top = _clipStack.Peek();
        var ix0 = Math.Max(top.X, x);
        var iy0 = Math.Max(top.Y, y);
        var ix1 = Math.Min(top.X + top.Width, x + w);
        var iy1 = Math.Min(top.Y + top.Height, y + h);
        var iw = Math.Max(0, ix1 - ix0);
        var ih = Math.Max(0, iy1 - iy0);
        _clipStack.Push((ix0, iy0, iw, ih));
        ApplyClipState();
    }

    private void PopClipRect()
    {
        if (_clipStack.Count == 0)
        {
            return;
        }

        _clipStack.Pop();
        ApplyClipState();
    }

    private void ApplyClipState()
    {
        if (_clipStack.Count == 0)
        {
            _gl.Disable(EnableCap.ScissorTest);
            return;
        }

        _gl.Enable(EnableCap.ScissorTest);
        var clip = _clipStack.Peek();
        var x = Math.Clamp(clip.X, 0, Math.Max(0, _viewportWidth));
        var y = Math.Clamp(clip.Y, 0, Math.Max(0, _viewportHeight));
        var w = Math.Clamp(clip.Width, 0, Math.Max(0, _viewportWidth - x));
        var h = Math.Clamp(clip.Height, 0, Math.Max(0, _viewportHeight - y));

        // glScissor uses bottom-left origin.
        var scissorY = Math.Max(0, _viewportHeight - (y + h));
        _gl.Scissor(x, scissorY, (uint)w, (uint)h);
    }

    private void DrawVertices(DrawBatchKind kind, List<float> vertices, GlyphAtlasData atlas)
    {
        if (vertices.Count == 0)
        {
            return;
        }

        var program = kind switch
        {
            DrawBatchKind.Glyph => _glyphProgram,
            DrawBatchKind.Quad => _quadProgram,
            DrawBatchKind.Tri3D => _tri3dProgram,
            _ => _quadProgram
        };

        var uViewportLocation = kind switch
        {
            DrawBatchKind.Glyph => _uViewportLocationGlyph,
            DrawBatchKind.Quad => _uViewportLocationQuad,
            DrawBatchKind.Tri3D => _uViewportLocationTri3d,
            _ => _uViewportLocationQuad
        };

        _gl.UseProgram(program);
        _gl.Uniform2(uViewportLocation, (float)_viewportWidth, (float)_viewportHeight);

        if (kind == DrawBatchKind.Glyph)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _atlasTexture);
            _gl.Uniform1(_uAtlasLocationGlyph, 0);
        }

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var data = vertices.ToArray();
        unsafe
        {
            fixed (float* ptr = data)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, BufferUsageARB.StreamDraw);
            }
        }
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(vertices.Count / FloatsPerVertex));
    }

    private void SetColorWrite(bool enabled)
    {
        _gl.ColorMask(enabled, enabled, enabled, enabled);
    }

    private void SetDepthState(bool enabled, bool writeEnabled, DepthFuncKind func)
    {
        if (enabled)
        {
            _gl.Enable(EnableCap.DepthTest);
        }
        else
        {
            _gl.Disable(EnableCap.DepthTest);
        }

        _gl.DepthMask(writeEnabled);
        _gl.DepthFunc(func switch
        {
            DepthFuncKind.Less => DepthFunction.Less,
            DepthFuncKind.Lequal => DepthFunction.Lequal,
            DepthFuncKind.Greater => DepthFunction.Greater,
            DepthFuncKind.Always => DepthFunction.Always,
            _ => DepthFunction.Less
        });
    }

    private void ClearDepth(float depth01)
    {
        depth01 = Math.Clamp(depth01, 0f, 1f);
        _gl.ClearDepth(depth01);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
    }

    private void AddGlyphRunVertices(List<float> vertices, DrawGlyphRun glyphRun, GlyphAtlasData atlas)
    {
        foreach (var glyph in glyphRun.Glyphs)
        {
            if (!atlas.TryGetMetrics(glyph.Codepoint, out var metrics))
            {
                continue;
            }

            if (metrics.Width == 0 || metrics.Height == 0)
            {
                continue;
            }

            var x0 = glyph.X + glyphRun.X;
            var y0 = glyph.Y + glyphRun.Y;
            var x1 = x0 + metrics.Width;
            var y1 = y0 + metrics.Height;

            var u0 = metrics.AtlasX / (float)atlas.Width;
            var u1 = (metrics.AtlasX + metrics.Width) / (float)atlas.Width;
            var v0 = metrics.AtlasY / (float)atlas.Height;
            var v1 = (metrics.AtlasY + metrics.Height) / (float)atlas.Height;

            var (r, g, b, a) = ToColor(glyph.Rgba);

            AddVertex(vertices, x0, y0, u0, v0, r, g, b, a);
            AddVertex(vertices, x1, y0, u1, v0, r, g, b, a);
            AddVertex(vertices, x1, y1, u1, v1, r, g, b, a);

            AddVertex(vertices, x0, y0, u0, v0, r, g, b, a);
            AddVertex(vertices, x1, y1, u1, v1, r, g, b, a);
            AddVertex(vertices, x0, y1, u0, v1, r, g, b, a);
        }
    }

    private static void AddQuadVertices(List<float> vertices, DrawQuad quad)
    {
        if (quad.Width <= 0 || quad.Height <= 0)
        {
            return;
        }

        var x0 = quad.X;
        var y0 = quad.Y;
        var x1 = x0 + quad.Width;
        var y1 = y0 + quad.Height;

        const float u = 0f;
        const float v = 0f;
        var (r, g, b, a) = ToColor(quad.Rgba);

        AddVertex(vertices, x0, y0, u, v, r, g, b, a);
        AddVertex(vertices, x1, y0, u, v, r, g, b, a);
        AddVertex(vertices, x1, y1, u, v, r, g, b, a);

        AddVertex(vertices, x0, y0, u, v, r, g, b, a);
        AddVertex(vertices, x1, y1, u, v, r, g, b, a);
        AddVertex(vertices, x0, y1, u, v, r, g, b, a);
    }

    private static void AddTriangleVertices(List<float> vertices, DrawTriangles triangles)
    {
        if (triangles.Vertices.Count == 0)
        {
            return;
        }

        const float u = 0f;
        const float v = 0f;
        foreach (var vertex in triangles.Vertices)
        {
            var (r, g, b, a) = ToColor(vertex.Rgba);
            AddVertex(vertices, vertex.X, vertex.Y, u, v, r, g, b, a);
        }
    }

    private static void AddTriangle3DVertices(List<float> vertices, DrawTriangles3D triangles)
    {
        if (triangles.Vertices.Count == 0)
        {
            return;
        }

        const float v = 0f;
        foreach (var vertex in triangles.Vertices)
        {
            var (r, g, b, a) = ToColor(vertex.Rgba);
            // Pack depth01 into the "u" slot; Vertex3D reads it from aDepth.x.
            AddVertex(vertices, vertex.X, vertex.Y, vertex.Depth01, v, r, g, b, a);
        }
    }

    private void DrawVignette(DrawVignetteQuad vignette)
    {
        if (vignette.Width <= 0 || vignette.Height <= 0)
        {
            return;
        }

        _gl.UseProgram(_vignetteProgram);
        _gl.Uniform2(_uViewportLocationVignette, (float)_viewportWidth, (float)_viewportHeight);

        var rect = new System.Numerics.Vector4(vignette.X, vignette.Y, vignette.Width, vignette.Height);
        _gl.Uniform4(_uRectLocationVignette, ref rect);

        var p = new System.Numerics.Vector3(
            Math.Clamp(vignette.Strength, 0f, 1f),
            Math.Clamp(vignette.Inner, 0f, 2f),
            Math.Clamp(vignette.Outer, 0f, 2f));
        _gl.Uniform3(_uParamsLocationVignette, ref p);

        var verts = new List<float>();
        AddQuadVertices(verts, new DrawQuad(vignette.X, vignette.Y, vignette.Width, vignette.Height, unchecked((int)0x000000FF)));
        if (verts.Count == 0)
        {
            return;
        }

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var data = verts.ToArray();
        unsafe
        {
            fixed (float* ptr = data)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, BufferUsageARB.StreamDraw);
            }
        }

        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(verts.Count / FloatsPerVertex));
    }

    private static void AddVertex(List<float> vertices, float x, float y, float u, float v, float r, float g, float b, float a)
    {
        vertices.Add(x);
        vertices.Add(y);
        vertices.Add(u);
        vertices.Add(v);
        vertices.Add(r);
        vertices.Add(g);
        vertices.Add(b);
        vertices.Add(a);
    }

    private static (float R, float G, float B, float A) ToColor(int rgba)
    {
        var r = (byte)((rgba >> 24) & 0xFF);
        var g = (byte)((rgba >> 16) & 0xFF);
        var b = (byte)((rgba >> 8) & 0xFF);
        var a = (byte)(rgba & 0xFF);

        return (r / 255f, g / 255f, b / 255f, a / 255f);
    }

    private uint CreateAtlasTexture(GlyphAtlasData atlas)
    {
        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

        unsafe
        {
            fixed (byte* ptr = atlas.Pixels)
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    InternalFormat.Rgba8,
                    (uint)atlas.Width,
                    (uint)atlas.Height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    ptr);
            }
        }

        return texture;
    }

    private uint CreateProgram(string vertexSource, string fragmentSource)
    {
        var vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        var fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
        {
            var infoLog = _gl.GetProgramInfoLog(program);
            throw new InvalidOperationException($"Program link failed: {infoLog}");
        }

        _gl.DetachShader(program, vertexShader);
        _gl.DetachShader(program, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        return program;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
        {
            var infoLog = _gl.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"Shader compile failed: {infoLog}");
        }

        return shader;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_atlasTexture != 0)
        {
            _gl.DeleteTexture(_atlasTexture);
        }

        if (_vbo != 0)
        {
            _gl.DeleteBuffer(_vbo);
        }

        if (_vao != 0)
        {
            _gl.DeleteVertexArray(_vao);
        }

        if (_glyphProgram != 0) _gl.DeleteProgram(_glyphProgram);
        if (_quadProgram != 0) _gl.DeleteProgram(_quadProgram);
        if (_tri3dProgram != 0) _gl.DeleteProgram(_tri3dProgram);
        if (_vignetteProgram != 0) _gl.DeleteProgram(_vignetteProgram);

        _disposed = true;
    }
}
