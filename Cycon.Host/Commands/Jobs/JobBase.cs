using System;
using System.Threading;
using System.Threading.Tasks;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Commands.Jobs;

public abstract class JobBase : IJob
{
    private JobState _state = JobState.Created;

    protected JobBase(JobId id, string kind, IEventSink events)
    {
        Id = id;
        Kind = kind;
        Events = events;
    }

    public JobId Id { get; }
    public string Kind { get; }
    public JobState State => _state;

    protected IEventSink Events { get; }

    public async Task RunAsync(CancellationToken ct)
    {
        if (_state != JobState.Created)
        {
            throw new InvalidOperationException("Job can only be run once.");
        }

        _state = JobState.Running;
        try
        {
            await RunCoreAsync(ct).ConfigureAwait(false);
            if (_state == JobState.Running)
            {
                _state = JobState.Completed;
            }
        }
        catch (OperationCanceledException)
        {
            _state = JobState.Cancelled;
            throw;
        }
        catch
        {
            _state = JobState.Failed;
            throw;
        }
    }

    protected abstract Task RunCoreAsync(CancellationToken ct);

    public virtual Task SendInputAsync(string text, CancellationToken ct) => Task.CompletedTask;

    public virtual Task RequestCancelAsync(CancelLevel level, CancellationToken ct) => Task.CompletedTask;

    protected void Publish(ConsoleEvent e) => Events.Publish(Id, e);
}

