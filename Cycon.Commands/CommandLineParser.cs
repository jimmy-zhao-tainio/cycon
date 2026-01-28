using System;
using System.Collections.Generic;
using System.Text;

namespace Cycon.Commands;

public static class CommandLineParser
{
    public static CommandRequest? Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var args = Tokenize(rawText);
        if (args.Count == 0)
        {
            return null;
        }

        var name = args[0];
        args.RemoveAt(0);
        return new CommandRequest(name, args, rawText);
    }

    public static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(input))
        {
            return tokens;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        void Flush()
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (inQuotes)
            {
                if (ch == quoteChar)
                {
                    inQuotes = false;
                    continue;
                }

                if (ch == '\\' && i + 1 < input.Length)
                {
                    var next = input[i + 1];
                    if (next == quoteChar || next == '\\')
                    {
                        // Special-case trailing backslash before a closing quote:
                        // `cd "C:\"` should parse as `C:\` (backslash is part of the path),
                        // not as an escaped quote.
                        if (next == quoteChar)
                        {
                            var afterQuoteIndex = i + 2;
                            var closesToken = afterQuoteIndex >= input.Length || char.IsWhiteSpace(input[afterQuoteIndex]);
                            if (closesToken)
                            {
                                current.Append('\\');
                                continue;
                            }
                        }

                        current.Append(next);
                        i++;
                        continue;
                    }
                }

                current.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                inQuotes = true;
                quoteChar = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                Flush();
                continue;
            }

            current.Append(ch);
        }

        Flush();
        return tokens;
    }
}
