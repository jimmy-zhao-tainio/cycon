using System;
using System.Threading;
using System.Threading.Tasks;
using Cycon.Commands;
using Cycon.Host.Commands.Jobs;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Commands.Handlers;

public sealed class AskCommandHandler : ICommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "ask",
        Summary: "Prompts for input and echoes it back (demo).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.Interactive | CommandCapabilities.Cancellable);

    public IJob Start(CommandRequest request, CommandStartContext context)
    {
        var prompt = request.Args.Count > 0 ? string.Join(" ", request.Args) + " " : "Input: ";
        return new AskJob(context.JobId, context.Events, prompt);
    }

    private sealed class AskJob : JobBase
    {
        private readonly TaskCompletionSource<string> _input = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _prompt;

        public AskJob(JobId id, IEventSink events, string prompt)
            : base(id, "builtin.ask", events)
        {
            _prompt = prompt;
        }

        protected override async Task RunCoreAsync(CancellationToken ct)
        {
            Publish(new PromptEvent(DateTimeOffset.UtcNow, _prompt, PromptKind.InputLine));
            using var reg = ct.Register(() => _input.TrySetCanceled(ct));
            var line = await _input.Task.ConfigureAwait(false);
            Publish(new ResultEvent(DateTimeOffset.UtcNow, 0, null));
        }

        public override Task SendInputAsync(string text, CancellationToken ct)
        {
            _input.TrySetResult(text);
            return Task.CompletedTask;
        }

        public override Task RequestCancelAsync(CancelLevel level, CancellationToken ct)
        {
            _input.TrySetCanceled(ct);
            Publish(new ResultEvent(DateTimeOffset.UtcNow, 130, "Cancelled"));
            return Task.CompletedTask;
        }
    }
}
