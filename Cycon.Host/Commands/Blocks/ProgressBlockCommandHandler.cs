using System;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript.Blocks;

namespace Cycon.Host.Commands.Blocks;

public sealed class ProgressBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "progress",
        Summary: "Simulates work with progress updates (native block).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.SupportsProgress);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        var duration = ParseDurationOrReport(request, ctx, "progress");
        if (duration is null)
        {
            return true;
        }

        var activityId = ctx.AllocateBlockId();
        ctx.InsertBlockAfterCommandEcho(new ActivityBlock(
            id: activityId,
            label: "progress",
            kind: ActivityKind.Progress,
            duration: duration.Value,
            stream: ConsoleTextStream.System));
        ctx.AttachIndicator(activityId);
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
