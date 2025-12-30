namespace Cycon.Core.Editing;

public sealed class PromptEditor
{
    public string Text { get; private set; } = string.Empty;

    public void SetText(string text) => Text = text ?? string.Empty;
}
