using System;
using System.Collections.Generic;
using Cycon.BlockCommands;

namespace Cycon.Host.Commands.Input;

public sealed class CommandCompletionProvider : IInputCompletionProvider
{
    private readonly BlockCommandRegistry _registry;

    public CommandCompletionProvider(BlockCommandRegistry registry)
    {
        _registry = registry;
    }

    public bool TryGetCandidates(in InputCompletionContext ctx, out IReadOnlyList<string> candidates)
    {
        var prefix = ctx.Prefix ?? string.Empty;
        var source = ctx.Mode switch
        {
            CompletionMode.CommandName => _registry.ListCommandNamesAndAliases(),
            CompletionMode.HelpTarget => _registry.ListHelpTargets(),
            _ => Array.Empty<string>()
        };

        if (source.Count == 0)
        {
            candidates = Array.Empty<string>();
            return false;
        }

        var matches = new List<string>();
        for (var i = 0; i < source.Count; i++)
        {
            if (source[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(source[i]);
            }
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        candidates = matches;
        return matches.Count > 0;
    }
}

