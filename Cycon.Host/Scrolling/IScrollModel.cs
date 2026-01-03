using Cycon.Core.Scrolling;
using Cycon.Layout.Scrolling;

namespace Cycon.Host.Scrolling;

public interface IScrollModel
{
    bool TryGetScrollbarLayout(PxRect viewportRectPx, ScrollbarSettings settings, out ScrollbarLayout layout);

    bool ApplyWheelDelta(int wheelDelta, PxRect viewportRectPx);

    bool DragThumbTo(int pointerYPx, int grabOffsetYPx, PxRect viewportRectPx, ScrollbarLayout layout);
}
