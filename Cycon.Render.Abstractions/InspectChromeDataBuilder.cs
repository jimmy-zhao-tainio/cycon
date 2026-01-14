using System;

namespace Cycon.Render;

public struct InspectChromeDataBuilder
{
    private readonly string[] _keys;
    private readonly string[] _values;
    private int _count;

    public InspectChromeDataBuilder(int capacity)
    {
        capacity = Math.Max(0, capacity);
        _keys = capacity == 0 ? Array.Empty<string>() : new string[capacity];
        _values = capacity == 0 ? Array.Empty<string>() : new string[capacity];
        _count = 0;
    }

    public int Count => _count;

    public void Clear()
    {
        _count = 0;
    }

    public void Set(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        for (var i = 0; i < _count; i++)
        {
            if (string.Equals(_keys[i], key, StringComparison.Ordinal))
            {
                _values[i] = value;
                return;
            }
        }

        if (_count >= _keys.Length)
        {
            return;
        }

        _keys[_count] = key;
        _values[_count] = value;
        _count++;
    }

    public bool TryGet(string key, out string value)
    {
        for (var i = 0; i < _count; i++)
        {
            if (string.Equals(_keys[i], key, StringComparison.Ordinal))
            {
                value = _values[i];
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
