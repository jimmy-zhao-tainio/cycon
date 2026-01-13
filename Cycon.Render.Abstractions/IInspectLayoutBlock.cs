namespace Cycon.Render;

public interface IInspectLayoutBlock
{
    void SetInspectLayoutEnabled(bool enabled);
    bool TryGetInspectViewport(out RectPx rect);
}
