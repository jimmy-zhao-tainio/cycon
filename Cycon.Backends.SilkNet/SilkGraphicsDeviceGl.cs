using Cycon.Backends.Abstractions;
using Silk.NET.OpenGL;

namespace Cycon.Backends.SilkNet;

public sealed class SilkGraphicsDeviceGl : IGraphicsDevice
{
    public SilkGraphicsDeviceGl(SilkWindow window)
    {
        Gl = GL.GetApi(window.Native);
    }

    public GL Gl { get; }
}
