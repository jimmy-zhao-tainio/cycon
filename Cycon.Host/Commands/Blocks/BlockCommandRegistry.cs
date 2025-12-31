using System;
using System.Collections.Generic;
using Cycon.Commands;

namespace Cycon.Host.Commands.Blocks;

public sealed class BlockCommandRegistry
{
    private readonly Dictionary<string, IBlockCommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IBlockCommandHandler> _aliases = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IBlockCommandHandler handler)
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

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        var handler = Resolve(request.Name);
        if (handler is null)
        {
            return false;
        }

        return handler.TryExecute(request, ctx);
    }

    private IBlockCommandHandler? Resolve(string nameOrAlias)
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
}

