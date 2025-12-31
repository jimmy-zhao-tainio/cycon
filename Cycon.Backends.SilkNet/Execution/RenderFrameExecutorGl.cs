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
    private uint _vao;
    private uint _vbo;
    private uint _atlasTexture;
    private int _viewportWidth;
    private int _viewportHeight;
    private int _uViewportLocationGlyph;
    private int _uAtlasLocationGlyph;
    private int _uViewportLocationQuad;
    private bool _initialized;
    private bool _disposed;
    private readonly bool _trace = Environment.GetEnvironmentVariable("CYCON_GL_TRACE") == "1";

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
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

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

        _gl.Clear(ClearBufferMask.ColorBufferBit);

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
        Quad
    }

    private void ExecuteBatched(RenderFrame frame, GlyphAtlasData atlas)
    {
        var commands = frame.Commands;
        var batches = DrawCommandBatcher.ComputeBatches(commands);

        DrawBatchKind? lastKind = null;
        foreach (var batch in batches)
        {
            var kind = batch.Kind == DrawCommandBatchKind.Glyph ? DrawBatchKind.Glyph : DrawBatchKind.Quad;
            if (_trace && (lastKind is null || lastKind.Value != kind))
            {
                Console.WriteLine($"[CYCON_GL_TRACE] switch -> {kind}");
            }

            if (_trace)
            {
                Console.WriteLine($"[CYCON_GL_TRACE] batch {kind} start={batch.StartIndex} count={batch.Count}");
            }

            var vertices = new List<float>();
            for (var i = batch.StartIndex; i < batch.StartIndex + batch.Count; i++)
            {
                switch (commands[i])
                {
                    case DrawGlyphRun glyphRun:
                        AddGlyphRunVertices(vertices, glyphRun, atlas);
                        break;
                    case DrawQuad quad:
                        AddQuadVertices(vertices, quad);
                        break;
                }
            }

            DrawVertices(kind, vertices, atlas);
            lastKind = kind;
        }
    }

    private void DrawVertices(DrawBatchKind kind, List<float> vertices, GlyphAtlasData atlas)
    {
        if (vertices.Count == 0)
        {
            return;
        }

        var program = kind == DrawBatchKind.Glyph ? _glyphProgram : _quadProgram;
        var uViewportLocation = kind == DrawBatchKind.Glyph ? _uViewportLocationGlyph : _uViewportLocationQuad;

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

        _disposed = true;
    }
}
