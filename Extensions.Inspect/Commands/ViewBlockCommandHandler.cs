using System;
using System.Collections.Generic;
using Cycon.BlockCommands;
using Cycon.Commands;

namespace Extensions.Inspect.Commands;

public sealed class ViewBlockCommandHandler : IBlockCommandHandler
{
    private readonly InspectBlockCommandHandler _inspect = new();

    public CommandSpec Spec { get; } = new(
        Name: "view",
        Summary: "Inspect file inline and spawn suitable block view.",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        var args = request.Args;
        var hasInline = false;
        var hasFullscreen = false;
        for (var i = 0; i < args.Count; i++)
        {
            hasInline |= args[i] is "--inline";
            hasFullscreen |= args[i] is "--fullscreen";
        }

        if (!hasInline && !hasFullscreen)
        {
            var next = new List<string>(args.Count + 1) { "--inline" };
            for (var i = 0; i < args.Count; i++)
            {
                next.Add(args[i]);
            }

            request = new CommandRequest("inspect", next, request.RawText);
        }

        return _inspect.TryExecute(request, ctx);
    }
}

