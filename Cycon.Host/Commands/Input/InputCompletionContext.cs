namespace Cycon.Host.Commands.Input;

public readonly record struct InputCompletionContext(
    CompletionMode Mode,
    string Prefix);

