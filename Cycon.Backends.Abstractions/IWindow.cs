namespace Cycon.Backends.Abstractions;

public interface IWindow
{
    int Width { get; }
    int Height { get; }
    int FramebufferWidth { get; }
    int FramebufferHeight { get; }
}
