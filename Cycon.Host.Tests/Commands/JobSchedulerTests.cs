using Cycon.Host.Commands;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Tests.Commands;

public sealed class JobSchedulerTests
{
    [Fact]
    public async Task DrainEvents_PreservesPerJobSequence()
    {
        var scheduler = new JobScheduler();
        var jobId = scheduler.AllocateJobId();
        var sink = scheduler.CreateEventSink(jobId);
        var job = new EmitJob(jobId, sink);
        scheduler.StartJob(job);

        var collected = new List<ConsoleEvent>();
        for (var i = 0; i < 200; i++)
        {
            var drained = scheduler.DrainEvents();
            foreach (var e in drained.Where(e => e.JobId == jobId))
            {
                collected.Add(e.Event);
            }

            if (collected.Any(e => e is ResultEvent))
            {
                break;
            }

            await Task.Delay(5);
        }

        var texts = collected.OfType<TextEvent>().Select(e => e.Text).ToList();
        Assert.Equal(new[] { "one", "two", "three" }, texts);
    }

    private sealed class EmitJob : IJob
    {
        private readonly IEventSink _sink;
        private readonly DateTimeOffset _ts = DateTimeOffset.UnixEpoch;
        private volatile JobState _state = JobState.Created;

        public EmitJob(JobId id, IEventSink sink)
        {
            Id = id;
            _sink = sink;
        }

        public JobId Id { get; }
        public string Kind => "test.emit";
        public JobState State => _state;

        public Task RunAsync(CancellationToken ct)
        {
            _state = JobState.Running;
            _sink.Publish(Id, new TextEvent(_ts, TextStream.Stdout, "one"));
            _sink.Publish(Id, new TextEvent(_ts, TextStream.Stdout, "two"));
            _sink.Publish(Id, new TextEvent(_ts, TextStream.Stdout, "three"));
            _sink.Publish(Id, new ResultEvent(_ts, 0, null));
            _state = JobState.Completed;
            return Task.CompletedTask;
        }

        public Task SendInputAsync(string text, CancellationToken ct) => Task.CompletedTask;
        public Task RequestCancelAsync(CancelLevel level, CancellationToken ct) => Task.CompletedTask;
    }
}
