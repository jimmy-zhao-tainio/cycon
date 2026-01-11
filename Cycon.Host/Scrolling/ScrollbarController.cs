using Cycon.Core;
using Cycon.Core.Settings;
using Cycon.Host.Input;
using Cycon.Host.Interaction;
using Cycon.Layout;
using Cycon.Layout.Scrolling;
using Cycon.Backends.Abstractions.Rendering;

namespace Cycon.Host.Scrolling;

internal sealed class ScrollbarController
{
    private readonly ConsoleDocument _document;
    private readonly ConsoleScrollModel _scrollModel;
    private readonly ScrollbarWidget _scrollbar;

    public ScrollbarController(ConsoleDocument document, LayoutSettings layoutSettings)
    {
        _document = document;
        _scrollModel = new ConsoleScrollModel(document, layoutSettings);
        _scrollbar = new ScrollbarWidget(_scrollModel, document.Scroll.ScrollbarUi);
    }

    public void BeginTick() => _scrollbar.BeginTick();

    public void OnPointerInWindowChanged(bool isInWindow) => _scrollbar.OnPointerInWindowChanged(isInWindow);

    public void AdvanceAnimation(int dtMs) => _scrollbar.AdvanceAnimation(dtMs, _document.Settings.Scrollbar);

    public void UpdateTotalRows(LayoutFrame? layout) => _scrollModel.TotalRows = layout?.TotalRows ?? 0;

    public RenderFrame? BuildOverlayFrame(PxRect viewportRectPx, int rgba) =>
        _scrollbar.BuildOverlayFrame(viewportRectPx, _document.Settings.Scrollbar, rgba, string.Empty);

    public bool TryHandleMouse(
        HostMouseEvent mouseEvent,
        PxRect viewportRectPx,
        InteractionSnapshot interactionSnapshot,
        LayoutFrame? layout,
        out bool scrollChanged)
    {
        scrollChanged = false;

        if (layout is null)
        {
            return false;
        }

        UpdateTotalRows(layout);

        if (!_document.Scroll.ScrollbarUi.IsDragging && interactionSnapshot.MouseCaptured is not null)
        {
            return false;
        }

        return _scrollbar.HandleMouse(mouseEvent, viewportRectPx, _document.Settings.Scrollbar, out scrollChanged);
    }
}
