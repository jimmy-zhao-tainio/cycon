namespace Cycon.Core.Transcript;

public interface ITextEditable
{
    void InsertText(string s);
    void Backspace();
    void MoveCaret(int delta);
    void SetCaret(int index);
}

