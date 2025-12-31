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
        var escaping = false;

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
            if (escaping)
            {
                current.Append(ch switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    '\'' => '\'',
                    _ => ch
                });
                escaping = false;
                continue;
            }

            if (ch == '\\')
            {
                escaping = true;
                continue;
            }

            if (inQuotes)
            {
                if (ch == quoteChar)
                {
                    inQuotes = false;
                    continue;
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

        if (escaping)
        {
            current.Append('\\');
        }

        Flush();
        return tokens;
    }
}

