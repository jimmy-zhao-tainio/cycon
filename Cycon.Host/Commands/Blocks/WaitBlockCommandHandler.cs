using System;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;

namespace Cycon.Host.Commands.Blocks;

public sealed class WaitBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "wait",
        Summary: "Simulates a long-running operation (native block).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        var duration = ParseDurationOrReport(request, ctx, "wait");
        if (duration is null)
        {
            return true;
        }

        var activityId = ctx.AllocateBlockId();
        ctx.InsertBlockAfterCommandEcho(new ActivityBlock(
            id: activityId,
            label: "wait",
            kind: ActivityKind.Wait,
            duration: duration.Value,
            stream: ConsoleTextStream.System));
        return true;
    }

    private static TimeSpan? ParseDurationOrReport(CommandRequest request, IBlockCommandContext ctx, string name)
    {
        if (request.Args.Count == 0)
        {
            return TimeSpan.FromSeconds(1);
        }

        if (request.Args.Count == 1 && int.TryParse(request.Args[0], out var ms) && ms >= 0)
        {
            return TimeSpan.FromMilliseconds(ms);
        }

        ctx.InsertTextAfterCommandEcho($"Usage: {name} [ms]", ConsoleTextStream.System);
        return null;
    }
}
