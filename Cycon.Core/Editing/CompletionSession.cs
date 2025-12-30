using System.Collections.Generic;

namespace Cycon.Core.Editing;

public sealed class CompletionSession
{
    public IReadOnlyList<string> Items { get; init; } = new List<string>();
}
