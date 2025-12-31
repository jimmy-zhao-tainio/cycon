using System;

namespace Cycon.Runtime.Events;

public abstract record ConsoleEvent(DateTimeOffset Timestamp);

public enum TextStream
{
    Stdout,
    Stderr,
    System
}

public sealed record TextEvent(DateTimeOffset Timestamp, TextStream Stream, string Text) : ConsoleEvent(Timestamp);

public sealed record ProgressEvent(DateTimeOffset Timestamp, double? Fraction, string? Phase) : ConsoleEvent(Timestamp);

public enum PromptKind
{
    InputLine,
    Password,
    Choice
}

public sealed record PromptEvent(DateTimeOffset Timestamp, string Prompt, PromptKind Kind) : ConsoleEvent(Timestamp);

public sealed record ResultEvent(DateTimeOffset Timestamp, int ExitCode, string? Summary) : ConsoleEvent(Timestamp);
