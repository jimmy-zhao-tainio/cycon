namespace Cycon.Backends.Abstractions;

public interface IClipboard
{
    void SetText(string text);
    string? GetText();
}
