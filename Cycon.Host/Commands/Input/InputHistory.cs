using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cycon.Host.Commands.Input;

public sealed class InputHistory
{
    private readonly string _path;
    private readonly int _maxEntries;
    private readonly List<string> _entries;
    private int _cursor;
    private string _draft;

    private InputHistory(string path, int maxEntries, List<string> entries)
    {
        _path = path;
        _maxEntries = Math.Max(1, maxEntries);
        _entries = entries;
        _cursor = _entries.Count;
        _draft = string.Empty;
    }

    public IReadOnlyList<string> Entries => _entries;

    public static InputHistory LoadDefault(int maxEntries = 1000)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Environment.CurrentDirectory;
        }

        var dir = Path.Combine(baseDir, "Cycon");
        var path = Path.Combine(dir, "history.txt");
        return Load(path, maxEntries);
    }

    public static InputHistory Load(string path, int maxEntries = 1000)
    {
        var entries = new List<string>();
        try
        {
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var trimmed = (line ?? string.Empty).Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    entries.Add(trimmed);
                }
            }
        }
        catch
        {
            entries.Clear();
        }

        if (entries.Count > maxEntries)
        {
            entries.RemoveRange(0, entries.Count - maxEntries);
        }

        return new InputHistory(path, maxEntries, entries);
    }

    public void ResetNavigation()
    {
        _cursor = _entries.Count;
        _draft = string.Empty;
    }

    public void RecordSubmitted(string input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            ResetNavigation();
            return;
        }

        if (_entries.Count == 0 || !string.Equals(_entries[^1], trimmed, StringComparison.Ordinal))
        {
            _entries.Add(trimmed);
            if (_entries.Count > _maxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - _maxEntries);
            }

            Save();
        }

        ResetNavigation();
    }

    public bool TryNavigate(string currentInput, int delta, out string newInput)
    {
        newInput = currentInput;
        if (delta == 0 || _entries.Count == 0)
        {
            return false;
        }

        _cursor = Math.Clamp(_cursor, 0, _entries.Count);

        if (delta < 0)
        {
            if (_cursor == _entries.Count)
            {
                _draft = currentInput ?? string.Empty;
            }

            if (_cursor == 0)
            {
                return false;
            }

            _cursor--;
            newInput = _entries[_cursor];
            return true;
        }

        if (delta > 0)
        {
            if (_cursor == _entries.Count)
            {
                return false;
            }

            _cursor++;
            newInput = _cursor == _entries.Count ? _draft : _entries[_cursor];
            return true;
        }

        return false;
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllLines(_path, _entries, Encoding.UTF8);
        }
        catch
        {
        }
    }
}

