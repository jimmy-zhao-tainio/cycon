using Cycon.Layout.Scrolling;

namespace Cycon.Host.Input;

public interface IBlockPointerHandler
{
    /// <summary>
    /// Handles pointer move/down/up events in screen (framebuffer) pixel coordinates.
    /// </summary>
    bool HandlePointer(in HostMouseEvent e, in PxRect viewportRectPx);
}

