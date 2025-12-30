using System.Collections.Generic;

namespace Cycon.Text.Words;

public static class WordBreaker
{
    public static IEnumerable<(int Start, int Length)> Break(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                if (start >= 0)
                {
                    yield return (start, i - start);
                    start = -1;
                }

                continue;
            }

            if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
        {
            yield return (start, text.Length - start);
        }
    }
}
