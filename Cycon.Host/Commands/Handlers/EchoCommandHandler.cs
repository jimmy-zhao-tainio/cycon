using System;
using System.Collections.Generic;
using Cycon.Commands;
using Cycon.Host.Commands.Jobs;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Commands.Handlers;

public sealed class EchoCommandHandler : ICommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "echo",
        Summary: "Prints text.",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.Cancellable);

    public IJob Start(CommandRequest request, CommandStartContext context)
    {
        return new EchoJob(context.JobId, context.Events, request.Args);
    }

    private sealed class EchoJob : JobBase
    {
        private readonly IReadOnlyList<string> _args;

        public EchoJob(JobId id, IEventSink events, IReadOnlyList<string> args)
            : base(id, "builtin.echo", events)
        {
            _args = args;
        }

        protected override Task RunCoreAsync(CancellationToken ct)
        {
            var text = _args.Count == 0 ? string.Empty : string.Join(" ", _args);
            Publish(new TextEvent(DateTimeOffset.UtcNow, TextStream.Stdout, text));
            Publish(new ResultEvent(DateTimeOffset.UtcNow, 0, null));
            return Task.CompletedTask;
        }
    }
}
