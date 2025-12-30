using System.Collections.Generic;

namespace Cycon.Text.Buffer;

public sealed class GapBuffer<T>
{
    private readonly List<T> _items = new();

    public int Count => _items.Count;

    public IReadOnlyList<T> Items => _items;

    public void Insert(int index, T value) => _items.Insert(index, value);

    public void RemoveAt(int index) => _items.RemoveAt(index);
}
