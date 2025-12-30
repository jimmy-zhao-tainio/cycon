using System.Collections.Generic;
using System.Globalization;

namespace Cycon.Text.Graphemes;

public static class GraphemeEnumerator
{
    public static IEnumerable<string> Enumerate(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }
}
