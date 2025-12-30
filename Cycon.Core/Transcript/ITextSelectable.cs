namespace Cycon.Core.Transcript;

public interface ITextSelectable
{
    bool CanSelect { get; }
    int TextLength { get; }
    string ExportText(int start, int length);
}

