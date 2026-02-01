using System;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Ai;
using Cycon.Host.Commands.Jobs;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Commands.Blocks;

public sealed class AiBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "ai",
        Summary: "Streams a mock AI response (no network).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.Interactive);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (ctx is BlockCommandContext hostContext && request.Args.Count == 0)
        {
            hostContext.ShowAiApiKeyOverlay();
            return true;
        }

        if (request.Args.Count == 0)
        {
            ctx.InsertTextAfterCommandEcho("Missing prompt.", ConsoleTextStream.System);
            return true;
        }

        var userPrompt = string.Join(" ", request.Args);

        var blockId = ctx.AllocateBlockId();
        ctx.InsertBlockAfterCommandEcho(new TextBlock(blockId, string.Empty, ConsoleTextStream.Default));

        if (ctx is not BlockCommandContext hostContext2)
        {
            ctx.InsertTextAfterCommandEcho("AI is unavailable.", ConsoleTextStream.System);
            return true;
        }

        var jobId = hostContext2.AllocateJobId();
        var events = hostContext2.CreateEventSink(jobId);
        var messages = new (string role, string text)[]
        {
            ("user", userPrompt)
        };

        var streamer = new MockAiStreamer();
        var job = new AiJob(jobId, blockId, events, streamer, messages);
        hostContext2.StartJob(job, JobOptions.Foreground);
        return true;
    }
}
