using System.Collections.Generic;

namespace Cycon.Host.Commands.Input;

public readonly record struct InputCompletionResult(
    int ReplaceStart,
    int ReplaceLength,
    IReadOnlyList<string> Candidates);

