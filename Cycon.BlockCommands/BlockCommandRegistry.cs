using System;
using System.Collections.Generic;
using Cycon.Commands;

namespace Cycon.BlockCommands;

public sealed class BlockCommandRegistry
{
    private readonly Dictionary<string, Registration> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Registration> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IBlockCommandFallbackHandler> _fallbacks = new();
    private readonly Dictionary<string, IHelpProvider> _helpProviders = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IBlockCommandHandler handler) => Register(handler, BlockCommandOrigin.Extension);

    public void RegisterCore(IBlockCommandHandler handler) => Register(handler, BlockCommandOrigin.Core);

    public void RegisterHelpProvider(IHelpProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Name))
        {
            throw new ArgumentException("Help provider name is required.", nameof(provider));
        }

        if (string.Equals(provider.Name, "help", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Extensions cannot register a help provider named 'help'.");
        }

        _helpProviders[provider.Name] = provider;
    }

    public IReadOnlyList<CommandSpec> ListCommands(BlockCommandOrigin origin)
    {
        var list = new List<CommandSpec>();
        foreach (var registration in _handlers.Values)
        {
            if (registration.Origin == origin)
            {
                list.Add(registration.Handler.Spec);
            }
        }

        list.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return list;
    }

    public IReadOnlyList<string> ListCommandNamesAndAliases()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in _handlers.Values)
        {
            set.Add(registration.Handler.Spec.Name);
            foreach (var alias in registration.Handler.Spec.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    set.Add(alias);
                }
            }
        }

        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public IReadOnlyList<string> ListHelpTargets()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ListCommandNamesAndAliases())
        {
            set.Add(name);
        }

        foreach (var provider in _helpProviders.Values)
        {
            if (!string.IsNullOrWhiteSpace(provider.Name))
            {
                set.Add(provider.Name);
            }
        }

        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public IReadOnlyList<IHelpProvider> ListHelpProviders()
    {
        var list = new List<IHelpProvider>(_helpProviders.Count);
        foreach (var provider in _helpProviders.Values)
        {
            list.Add(provider);
        }

        list.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return list;
    }

    public bool TryGetHelpProvider(string name, out IHelpProvider? provider)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            provider = null;
            return false;
        }

        return _helpProviders.TryGetValue(name, out provider);
    }

    public bool TryGetCommand(string nameOrAlias, out CommandSpec spec, out BlockCommandOrigin origin)
    {
        var registration = Resolve(nameOrAlias);
        if (registration is null)
        {
            spec = default!;
            origin = default;
            return false;
        }

        spec = registration.Value.Handler.Spec;
        origin = registration.Value.Origin;
        return true;
    }

    private void Register(IBlockCommandHandler handler, BlockCommandOrigin origin)
    {
        if (origin == BlockCommandOrigin.Extension &&
            string.Equals(handler.Spec.Name, "help", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Extensions cannot register their own 'help' command.");
        }

        var registration = new Registration(handler, origin);
        _handlers[handler.Spec.Name] = registration;
        foreach (var alias in handler.Spec.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _aliases[alias] = registration;
            }
        }
    }

    public void RegisterFallback(IBlockCommandFallbackHandler fallback) => _fallbacks.Add(fallback);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        var registration = Resolve(request.Name);
        if (registration is null)
        {
            return false;
        }

        return registration.Value.Handler.TryExecute(request, ctx);
    }

    public bool TryExecuteOrFallback(CommandRequest request, string rawInput, IBlockCommandContext ctx)
    {
        var registration = Resolve(request.Name);
        if (registration is not null)
        {
            return registration.Value.Handler.TryExecute(request, ctx);
        }

        for (var i = 0; i < _fallbacks.Count; i++)
        {
            if (_fallbacks[i].TryHandle(rawInput, ctx))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsRegistered(string nameOrAlias) => Resolve(nameOrAlias) is not null;

    private Registration? Resolve(string nameOrAlias)
    {
        if (string.IsNullOrWhiteSpace(nameOrAlias))
        {
            return null;
        }

        if (_handlers.TryGetValue(nameOrAlias, out var registration))
        {
            return registration;
        }

        return _aliases.TryGetValue(nameOrAlias, out registration) ? registration : null;
    }

    private readonly record struct Registration(IBlockCommandHandler Handler, BlockCommandOrigin Origin);
}
