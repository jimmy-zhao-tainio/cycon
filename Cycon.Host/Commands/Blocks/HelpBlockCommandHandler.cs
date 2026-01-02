using System;
using System.Collections.Generic;
using System.Linq;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;

namespace Cycon.Host.Commands.Blocks;

public sealed class HelpBlockCommandHandler : IBlockCommandHandler
{
    private readonly BlockCommandRegistry _registry;

    public HelpBlockCommandHandler(BlockCommandRegistry registry)
    {
        _registry = registry;
    }

    public CommandSpec Spec { get; } = new(
        Name: "help",
        Summary: "Shows help for commands and extensions.",
        Aliases: new[] { "?" },
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        var console = new ContextConsole(ctx);

        if (request.Args.Count == 0)
        {
            PrintIndex(console);
            return true;
        }

        if (request.Args.Count != 1)
        {
            PrintCommandHeader(console, Spec.Name);
            console.WriteLine("Usage");
            console.WriteLine("  help <name>");
            return true;
        }

        var topic = request.Args[0];
        if (_registry.TryGetCommand(topic, out var commandSpec, out var origin) && origin == BlockCommandOrigin.Core)
        {
            PrintCoreCommandHelp(console, commandSpec);
            return true;
        }

        if (_registry.TryGetHelpProvider(topic, out var provider))
        {
            provider!.PrintHelp(console);
            return true;
        }

        console.WriteLine($"Unknown help topic: {topic}");
        console.WriteLine("Type: help <name> for details");
        return true;
    }

    private void PrintIndex(IConsole console)
    {
        var commands = _registry.ListCommands(BlockCommandOrigin.Core);
        console.WriteLine("Commands");
        PrintNameList(console, commands.Select(static x => (x.Name, x.Summary)));
        console.WriteLine(string.Empty);

        var extensions = _registry.ListHelpProviders();
        console.WriteLine("Extensions");
        if (extensions.Count > 0)
        {
            PrintNameList(console, extensions.Select(static x => (x.Name, x.Summary)));
        }

        console.WriteLine(string.Empty);
        console.WriteLine("Type: help <name> for details");
    }

    private static void PrintCoreCommandHelp(IConsole console, CommandSpec spec)
    {
        PrintCommandHeader(console, spec.Name);
        console.WriteLine("Summary");
        console.WriteLine($"  {TrimSummary(spec.Summary)}");

        if (spec.Aliases.Count > 0)
        {
            console.WriteLine(string.Empty);
            console.WriteLine("Aliases");
            console.WriteLine($"  {string.Join(" ", spec.Aliases)}");
        }
    }

    private static void PrintCommandHeader(IConsole console, string name)
    {
        console.WriteLine(name);
        console.WriteLine(string.Empty);
    }

    private static void PrintNameList(IConsole console, IEnumerable<(string Name, string Summary)> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var width = 0;
        for (var i = 0; i < list.Count; i++)
        {
            width = Math.Max(width, list[i].Name.Length);
        }

        for (var i = 0; i < list.Count; i++)
        {
            var (name, summary) = list[i];
            console.WriteLine($"  {name.PadRight(width)}  {TrimSummary(summary)}");
        }
    }

    private static string TrimSummary(string summary)
    {
        var s = (summary ?? string.Empty).Trim();

        var markerIndex = s.IndexOf("(native block)", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            s = s[..markerIndex].TrimEnd();
        }

        s = s.TrimEnd();
        if (s.EndsWith(".", StringComparison.Ordinal))
        {
            s = s[..^1];
        }

        return s;
    }

    private sealed class ContextConsole : IConsole
    {
        private readonly IBlockCommandContext _ctx;

        public ContextConsole(IBlockCommandContext ctx)
        {
            _ctx = ctx;
        }

        public void WriteLine(string text)
        {
            _ctx.InsertTextAfterCommandEcho(text, ConsoleTextStream.System);
        }
    }
}
