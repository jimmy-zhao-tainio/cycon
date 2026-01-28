using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;
using Cycon.Runtime.Runtime;

namespace Cycon.Host.Commands;

public sealed class JobScheduler : IJobRuntime
{
    private readonly ConcurrentDictionary<JobId, IJob> _jobs = new();
    private readonly ConcurrentQueue<PublishedEvent> _events = new();
    private long _nextJobId;
    private long _arrivalIndex;
    private readonly ConcurrentDictionary<JobId, long> _eventSeq = new();

    private readonly CancelEscalationPolicy _cancelPolicy;
    private readonly ConcurrentDictionary<JobId, CancellationState> _cancelStates = new();
    private readonly Action? _wake;

    public JobScheduler(Action? wake = null, CancelEscalationPolicy? policy = null)
    {
        _wake = wake;
        _cancelPolicy = policy ?? CancelEscalationPolicy.Default;
    }

    public JobId AllocateJobId() => new(Interlocked.Increment(ref _nextJobId));

    public IEventSink CreateEventSink(JobId jobId) => new SchedulerEventSink(this, jobId);

    public void StartJob(IJob job)
    {
        if (!_jobs.TryAdd(job.Id, job))
        {
            throw new InvalidOperationException($"Job {job.Id} already exists.");
        }

        Task.Run(async () =>
        {
            try
            {
                await job.RunAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Publish(job.Id, new TextEvent(DateTimeOffset.UtcNow, TextStream.System, $"Job failed: {ex.Message}"));
                Publish(job.Id, new ResultEvent(DateTimeOffset.UtcNow, -1, "Failed"));
            }
        });
    }

    public bool TryGetJob(JobId jobId, out IJob job) => _jobs.TryGetValue(jobId, out job!);

    public void RequestCancel(JobId jobId, CancelLevel level)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return;
        }

        Task.Run(() => job.RequestCancelAsync(level, CancellationToken.None));
    }

    public IReadOnlyList<PublishedEvent> DrainEvents()
    {
        if (_events.IsEmpty)
        {
            return Array.Empty<PublishedEvent>();
        }

        var drained = new List<PublishedEvent>();
        while (_events.TryDequeue(out var e))
        {
            drained.Add(e);
        }

        drained.Sort(static (a, b) => a.ArrivalIndex.CompareTo(b.ArrivalIndex));

        return drained;
    }

    public void RequestCancelWithEscalation(JobId jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return;
        }

        var state = _cancelStates.GetOrAdd(jobId, _ => new CancellationState());
        if (state.FirstRequestTicks == 0)
        {
            state.FirstRequestTicks = StopwatchTicks();
            state.LastIssuedLevel = CancelLevel.Soft;
            Task.Run(() => job.RequestCancelAsync(CancelLevel.Soft, CancellationToken.None));
            StartEscalationLoop(jobId, job, state);
            return;
        }

        // Manual escalation on repeated cancel request.
        var next = state.LastIssuedLevel switch
        {
            CancelLevel.Soft => CancelLevel.Terminate,
            CancelLevel.Terminate => CancelLevel.Kill,
            _ => CancelLevel.Kill
        };

        if (next <= state.LastIssuedLevel)
        {
            return;
        }

        state.LastIssuedLevel = next;
        Task.Run(() => job.RequestCancelAsync(next, CancellationToken.None));
    }

    internal void Publish(JobId jobId, ConsoleEvent e)
    {
        var arrival = Interlocked.Increment(ref _arrivalIndex);
        var seq = _eventSeq.AddOrUpdate(jobId, 1, static (_, existing) => existing + 1);
        _events.Enqueue(new PublishedEvent(jobId, seq, arrival, e));
        _wake?.Invoke();
    }

    private sealed class SchedulerEventSink : IEventSink
    {
        private readonly JobScheduler _scheduler;
        private readonly JobId _jobId;

        public SchedulerEventSink(JobScheduler scheduler, JobId jobId)
        {
            _scheduler = scheduler;
            _jobId = jobId;
        }

        public void Publish(JobId jobId, ConsoleEvent e)
        {
            if (jobId != _jobId)
            {
                throw new InvalidOperationException("JobId mismatch for this event sink.");
            }

            _scheduler.Publish(jobId, e);
        }
    }

    private sealed class CancellationState
    {
        public long FirstRequestTicks;
        public CancelLevel LastIssuedLevel;
        public int EscalationStarted;
    }

    public readonly record struct PublishedEvent(JobId JobId, long Seq, long ArrivalIndex, ConsoleEvent Event);

    public sealed record CancelEscalationPolicy(int TerminateAfterMs, int KillAfterMs)
    {
        public static readonly CancelEscalationPolicy Default = new(800, 2500);
    }

    private static long StopwatchTicks() => System.Diagnostics.Stopwatch.GetTimestamp();

    private void StartEscalationLoop(JobId jobId, IJob job, CancellationState state)
    {
        if (Interlocked.Exchange(ref state.EscalationStarted, 1) != 0)
        {
            return;
        }

        Task.Run(async () =>
        {
            if (_cancelPolicy.TerminateAfterMs > 0)
            {
                await Task.Delay(_cancelPolicy.TerminateAfterMs).ConfigureAwait(false);
                if (job.State == JobState.Running && state.LastIssuedLevel < CancelLevel.Terminate)
                {
                    state.LastIssuedLevel = CancelLevel.Terminate;
                    await job.RequestCancelAsync(CancelLevel.Terminate, CancellationToken.None).ConfigureAwait(false);
                }
            }

            if (_cancelPolicy.KillAfterMs > 0)
            {
                await Task.Delay(Math.Max(0, _cancelPolicy.KillAfterMs - _cancelPolicy.TerminateAfterMs)).ConfigureAwait(false);
                if (job.State == JobState.Running && state.LastIssuedLevel < CancelLevel.Kill)
                {
                    state.LastIssuedLevel = CancelLevel.Kill;
                    await job.RequestCancelAsync(CancelLevel.Kill, CancellationToken.None).ConfigureAwait(false);
                }
            }
        });
    }
}
