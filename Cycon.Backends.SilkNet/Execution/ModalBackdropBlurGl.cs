using System;
using Silk.NET.OpenGL;

namespace Cycon.Backends.SilkNet.Execution;

internal sealed class ModalBackdropBlurGl : IDisposable
{
    // Tuning knobs (keep centralized; no scattered magic numbers).
    public const int Levels = 3; // 1/2, 1/4, 1/8

    // Offsets are in source texels per pass. Keep deterministic to avoid shimmer.
    // Slightly larger offsets at lower resolutions read as optical defocus without smearing.
    private static readonly float[] OffsetsDown = { 1.25f, 1.35f, 1.45f };
    private static readonly float[] OffsetsUp = { 1.15f, 1.05f, 0.95f };

    private readonly GL _gl;

    private uint _vao;
    private uint _vbo;

    private RenderTarget _src;
    private RenderTarget _blurFull;
    private readonly RenderTarget[] _levels = new RenderTarget[Levels];

    private bool _hasCachedBlur;
    private long _cachedKey;

    private bool _capturing;
    private bool _skipCapture;

    private uint _kawaseProgram;
    private int _uViewportKawase;
    private int _uImageKawase;
    private int _uTexelKawase;
    private int _uOffsetKawase;

    private uint _imageProgram;
    private int _uViewportImage;
    private int _uImageImage;

    private readonly record struct RenderTarget(uint Fbo, uint Tex, int W, int H);

    public ModalBackdropBlurGl(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
    }

    public void SetPrograms(
        uint imageProgram,
        int uViewportImage,
        int uImageImage,
        uint kawaseProgram,
        int uViewportKawase,
        int uImageKawase,
        int uTexelKawase,
        int uOffsetKawase)
    {
        _imageProgram = imageProgram;
        _uViewportImage = uViewportImage;
        _uImageImage = uImageImage;

        _kawaseProgram = kawaseProgram;
        _uViewportKawase = uViewportKawase;
        _uImageKawase = uImageKawase;
        _uTexelKawase = uTexelKawase;
        _uOffsetKawase = uOffsetKawase;
    }

    public bool BeginCapture(int screenW, int screenH, long cacheKey)
    {
        EnsureGeometry();
        EnsureTargets(screenW, screenH);

        _skipCapture = _hasCachedBlur && _cachedKey == cacheKey && _blurFull.W == screenW && _blurFull.H == screenH;
        _capturing = !_skipCapture;

        if (_skipCapture)
        {
            return true;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _src.Fbo);
        _gl.Viewport(0, 0, (uint)_src.W, (uint)_src.H);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        return false;
    }

    public void EndCapture(int screenW, int screenH)
    {
        if (!_capturing)
        {
            return;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)screenW, (uint)screenH);
        _capturing = false;
    }

    public void Present(int screenW, int screenH, long cacheKey)
    {
        EnsureGeometry();
        EnsureTargets(screenW, screenH);

        if (!_hasCachedBlur || _cachedKey != cacheKey || _blurFull.W != screenW || _blurFull.H != screenH)
        {
            // If capture was skipped but cache doesn't match, fall back to whatever is currently in src.
            RunBlur(screenW, screenH);
            _cachedKey = cacheKey;
            _hasCachedBlur = true;
        }

        // Present blurred background to the default framebuffer.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)screenW, (uint)screenH);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);

        DrawFullscreenTextured(_imageProgram, _uViewportImage, _uImageImage, _blurFull.Tex, screenW, screenH, srcW: _blurFull.W, srcH: _blurFull.H);
    }

    private void RunBlur(int screenW, int screenH)
    {
        if (_kawaseProgram == 0)
        {
            return;
        }

        // Downsample chain.
        var inputTex = _src.Tex;
        var inputW = _src.W;
        var inputH = _src.H;

        for (var i = 0; i < Levels; i++)
        {
            var dst = _levels[i];
            BindTarget(dst);
            DrawKawase(inputTex, inputW, inputH, dst.W, dst.H, OffsetsDown[i]);
            inputTex = dst.Tex;
            inputW = dst.W;
            inputH = dst.H;
        }

        // Upsample back.
        for (var i = Levels - 1; i >= 0; i--)
        {
            var dst = i == 0 ? _blurFull : _levels[i - 1];
            BindTarget(dst);
            DrawKawase(inputTex, inputW, inputH, dst.W, dst.H, OffsetsUp[Levels - 1 - i]);
            inputTex = dst.Tex;
            inputW = dst.W;
            inputH = dst.H;
        }

        // Final stage writes to _blurFull.
        if (_blurFull.W != screenW || _blurFull.H != screenH)
        {
            // Defensive: ensure full-size target matches screen.
            EnsureTargets(screenW, screenH);
        }
    }

    private void BindTarget(in RenderTarget dst)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, dst.Fbo);
        _gl.Viewport(0, 0, (uint)dst.W, (uint)dst.H);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    private void DrawKawase(uint srcTex, int srcW, int srcH, int dstW, int dstH, float offset)
    {
        if (dstW <= 0 || dstH <= 0 || srcW <= 0 || srcH <= 0)
        {
            return;
        }

        _gl.UseProgram(_kawaseProgram);
        _gl.Uniform2(_uViewportKawase, (float)dstW, (float)dstH);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, srcTex);
        _gl.Uniform1(_uImageKawase, 0);
        _gl.Uniform2(_uTexelKawase, 1f / srcW, 1f / srcH);
        _gl.Uniform1(_uOffsetKawase, offset);

        DrawFullscreenGeometry(dstW, dstH);
    }

    private void DrawFullscreenTextured(uint program, int uViewport, int uImage, uint tex, int dstW, int dstH, int srcW, int srcH)
    {
        if (dstW <= 0 || dstH <= 0 || tex == 0)
        {
            return;
        }

        _gl.UseProgram(program);
        _gl.Uniform2(uViewport, (float)dstW, (float)dstH);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.Uniform1(uImage, 0);

        DrawFullscreenGeometry(dstW, dstH);
    }

    private unsafe void EnsureGeometry()
    {
        if (_vao != 0 && _vbo != 0)
        {
            return;
        }

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        const int floatsPerVertex = 8;
        var strideBytes = floatsPerVertex * sizeof(float);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)strideBytes, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)strideBytes, (void*)(2 * sizeof(float)));

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, (uint)strideBytes, (void*)(4 * sizeof(float)));
    }

    private void DrawFullscreenGeometry(int w, int h)
    {
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        // 2 triangles covering 0..w, 0..h with uv 0..1, color white.
        var x0 = 0f;
        var y0 = 0f;
        var x1 = (float)w;
        var y1 = (float)h;

        var verts = new float[]
        {
            x0, y0, 0f, 0f, 1f, 1f, 1f, 1f,
            x1, y0, 1f, 0f, 1f, 1f, 1f, 1f,
            x1, y1, 1f, 1f, 1f, 1f, 1f, 1f,

            x0, y0, 0f, 0f, 1f, 1f, 1f, 1f,
            x1, y1, 1f, 1f, 1f, 1f, 1f, 1f,
            x0, y1, 0f, 1f, 1f, 1f, 1f, 1f,
        };

        unsafe
        {
            fixed (float* ptr = verts)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)), ptr, BufferUsageARB.StreamDraw);
            }
        }

        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    private void EnsureTargets(int w, int h)
    {
        w = Math.Max(1, w);
        h = Math.Max(1, h);

        if (_src.W == w && _src.H == h && _src.Fbo != 0 && _blurFull.Fbo != 0)
        {
            return;
        }

        ReleaseTargets();

        _src = CreateTarget(w, h);
        _blurFull = CreateTarget(w, h);

        var lw = w;
        var lh = h;
        for (var i = 0; i < Levels; i++)
        {
            lw = Math.Max(1, lw / 2);
            lh = Math.Max(1, lh / 2);
            _levels[i] = CreateTarget(lw, lh);
        }

        _hasCachedBlur = false;
        _cachedKey = 0;
    }

    private RenderTarget CreateTarget(int w, int h)
    {
        var tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        }

        var fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, tex, 0);
        _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Backdrop blur FBO incomplete: {status}");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return new RenderTarget(fbo, tex, w, h);
    }

    private void ReleaseTargets()
    {
        ReleaseTarget(_src);
        ReleaseTarget(_blurFull);
        for (var i = 0; i < _levels.Length; i++)
        {
            ReleaseTarget(_levels[i]);
            _levels[i] = default;
        }

        _src = default;
        _blurFull = default;
    }

    private void ReleaseTarget(in RenderTarget t)
    {
        if (t.Fbo != 0)
        {
            _gl.DeleteFramebuffer(t.Fbo);
        }

        if (t.Tex != 0)
        {
            _gl.DeleteTexture(t.Tex);
        }
    }

    public void Dispose()
    {
        ReleaseTargets();

        if (_vbo != 0)
        {
            _gl.DeleteBuffer(_vbo);
            _vbo = 0;
        }

        if (_vao != 0)
        {
            _gl.DeleteVertexArray(_vao);
            _vao = 0;
        }
    }
}
