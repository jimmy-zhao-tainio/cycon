using System.Collections.Generic;

namespace Cycon.Core.Editing;

public sealed class HistoryBuffer
{
    private readonly List<string> _entries = new();

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string entry)
    {
        if (!string.IsNullOrEmpty(entry))
        {
            _entries.Add(entry);
        }
    }
}
