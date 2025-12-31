using System;
using System.Collections.Generic;

namespace Cycon.Commands;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ICommandHandler> _aliases = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ICommandHandler handler)
    {
        _handlers[handler.Spec.Name] = handler;
        foreach (var alias in handler.Spec.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _aliases[alias] = handler;
            }
        }
    }

    public ICommandHandler? Resolve(string nameOrAlias)
    {
        if (string.IsNullOrWhiteSpace(nameOrAlias))
        {
            return null;
        }

        if (_handlers.TryGetValue(nameOrAlias, out var handler))
        {
            return handler;
        }

        return _aliases.TryGetValue(nameOrAlias, out handler) ? handler : null;
    }

    public IReadOnlyList<CommandSpec> List()
    {
        var list = new List<CommandSpec>(_handlers.Count);
        foreach (var handler in _handlers.Values)
        {
            list.Add(handler.Spec);
        }

        list.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }
}
