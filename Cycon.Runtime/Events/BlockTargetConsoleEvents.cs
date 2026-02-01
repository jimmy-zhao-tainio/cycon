using System;

namespace Cycon.Runtime.Events;

/// <summary>
/// Generic, block-targeted console events intended for jobs that stream updates into an existing transcript block.
/// These events are applied by the normal job event pipeline (JobEventApplier).
/// </summary>
public abstract record BlockTargetConsoleEvent(DateTimeOffset Timestamp, int TargetBlockId) : ConsoleEvent(Timestamp);

public sealed record BlockTargetStatusEvent(DateTimeOffset Timestamp, int TargetBlockId, string Status)
    : BlockTargetConsoleEvent(Timestamp, TargetBlockId);

public sealed record BlockTargetTextDeltaEvent(DateTimeOffset Timestamp, int TargetBlockId, string Delta)
    : BlockTargetConsoleEvent(Timestamp, TargetBlockId);

public sealed record BlockTargetCompletedEvent(DateTimeOffset Timestamp, int TargetBlockId)
    : BlockTargetConsoleEvent(Timestamp, TargetBlockId);

public sealed record BlockTargetCancelledEvent(DateTimeOffset Timestamp, int TargetBlockId)
    : BlockTargetConsoleEvent(Timestamp, TargetBlockId);

public sealed record BlockTargetErrorEvent(DateTimeOffset Timestamp, int TargetBlockId, string Message)
    : BlockTargetConsoleEvent(Timestamp, TargetBlockId);

