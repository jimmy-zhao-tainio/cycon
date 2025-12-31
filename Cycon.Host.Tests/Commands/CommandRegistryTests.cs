using Cycon.Commands;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Tests.Commands;

public sealed class CommandRegistryTests
{
    [Fact]
    public void Resolve_ByNameOrAlias_Works()
    {
        var registry = new CommandRegistry();
        registry.Register(new AliasHandler());

        Assert.NotNull(registry.Resolve("alias"));
        Assert.NotNull(registry.Resolve("a"));
        Assert.Null(registry.Resolve("missing"));
    }

    private sealed class AliasHandler : ICommandHandler
    {
        public CommandSpec Spec { get; } = new("alias", "test", new[] { "a" }, CommandCapabilities.None);

        public IJob Start(CommandRequest request, CommandStartContext context) => new NoopJob(context.JobId);

        private sealed class NoopJob : IJob
        {
            public NoopJob(JobId id) => Id = id;
            public JobId Id { get; }
            public string Kind => "noop";
            public JobState State => JobState.Completed;
            public Task RunAsync(CancellationToken ct) => Task.CompletedTask;
            public Task SendInputAsync(string text, CancellationToken ct) => Task.CompletedTask;
            public Task RequestCancelAsync(CancelLevel level, CancellationToken ct) => Task.CompletedTask;
        }
    }
}

