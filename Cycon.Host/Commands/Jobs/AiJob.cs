using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cycon.Core.Transcript;
using Cycon.Host.Ai;
using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Host.Commands.Jobs;

internal sealed class AiJob : IJob
{
    private readonly IEventSink _events;
    private readonly MockAiStreamer _streamer;
    private readonly IReadOnlyList<(string role, string text)> _messages;
    private readonly CancellationTokenSource _cts = new();
    private volatile JobState _state = JobState.Created;

    public AiJob(
        JobId id,
        BlockId targetBlockId,
        IEventSink events,
        MockAiStreamer streamer,
        IReadOnlyList<(string role, string text)> messages)
    {
        Id = id;
        TargetBlockId = targetBlockId;
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _streamer = streamer ?? throw new ArgumentNullException(nameof(streamer));
        _messages = messages ?? Array.Empty<(string role, string text)>();
    }

    public JobId Id { get; }
    public string Kind => "ai";
    public JobState State => _state;
    public BlockId TargetBlockId { get; }

    public async Task RunAsync(CancellationToken ct)
    {
        if (_state != JobState.Created)
        {
            return;
        }

        _state = JobState.Running;

        var sink = new AiEventSink(Id, _events, TargetBlockId);
        try
        {
            await _streamer.StreamOnceAsync(_messages, sink, _cts.Token).ConfigureAwait(false);
            _state = sink.HadError ? JobState.Failed : JobState.Completed;
        }
        catch (OperationCanceledException)
        {
            if (_state == JobState.Running)
            {
                _events.Publish(Id, new BlockTargetCancelledEvent(DateTimeOffset.UtcNow, TargetBlockId.Value));
            }

            _state = JobState.Cancelled;
        }
        catch (Exception ex)
        {
            _events.Publish(Id, new BlockTargetErrorEvent(DateTimeOffset.UtcNow, TargetBlockId.Value, ex.Message));
            _state = JobState.Failed;
        }
    }

    public Task SendInputAsync(string text, CancellationToken ct) => Task.CompletedTask;

    public Task RequestCancelAsync(CancelLevel level, CancellationToken ct)
    {
        if (_state != JobState.Running)
        {
            return Task.CompletedTask;
        }

        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    private sealed class AiEventSink : IAiStreamSink
    {
        private readonly JobId _jobId;
        private readonly IEventSink _events;
        private readonly BlockId _targetBlockId;
        private int _completed;

        public AiEventSink(JobId jobId, IEventSink events, BlockId targetBlockId)
        {
            _jobId = jobId;
            _events = events;
            _targetBlockId = targetBlockId;
        }

        public bool HadError { get; private set; }

        public void Status(string shortStatus)
        {
            if (Volatile.Read(ref _completed) != 0)
            {
                return;
            }

            _events.Publish(_jobId, new BlockTargetStatusEvent(DateTimeOffset.UtcNow, _targetBlockId.Value, shortStatus ?? string.Empty));
        }

        public void TextDelta(string delta)
        {
            if (Volatile.Read(ref _completed) != 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            _events.Publish(_jobId, new BlockTargetTextDeltaEvent(DateTimeOffset.UtcNow, _targetBlockId.Value, delta));
        }

        public void Completed()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            _events.Publish(_jobId, new BlockTargetCompletedEvent(DateTimeOffset.UtcNow, _targetBlockId.Value));
        }

        public void Error(string message)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            HadError = true;
            _events.Publish(_jobId, new BlockTargetErrorEvent(DateTimeOffset.UtcNow, _targetBlockId.Value, message ?? string.Empty));
        }
    }
}
