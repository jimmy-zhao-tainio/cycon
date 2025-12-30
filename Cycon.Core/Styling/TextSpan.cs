namespace Cycon.Core.Styling;

public sealed class TextSpan
{
    public TextSpan(string text, TextStyle style)
    {
        Text = text;
        Style = style;
    }

    public string Text { get; }
    public TextStyle Style { get; }
}
