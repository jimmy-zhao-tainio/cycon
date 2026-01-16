namespace Cycon.Core.Transcript;

public interface IBlockTextSelection
{
    bool HasSelection { get; }
    bool TryGetSelectedText(out string text);
    void ClearSelection();
    void SelectAll();
}

