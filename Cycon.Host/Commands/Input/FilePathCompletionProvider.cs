using System;
using System.Collections.Generic;
using System.IO;

namespace Cycon.Host.Commands.Input;

public sealed class FilePathCompletionProvider : IInputCompletionProvider
{
    public bool TryGetCandidates(in InputCompletionContext ctx, out IReadOnlyList<string> candidates)
    {
        if (ctx.Mode != CompletionMode.FilePath)
        {
            candidates = Array.Empty<string>();
            return false;
        }

        var prefix = ctx.Prefix ?? string.Empty;
        if (!TryGetDirectoryAndNamePrefix(prefix, out var baseDir, out var dirPrefixForInsert, out var namePrefix))
        {
            candidates = Array.Empty<string>();
            return false;
        }

        List<(string Name, bool IsDirectory)> entries;
        try
        {
            entries = EnumerateEntries(baseDir);
        }
        catch
        {
            candidates = Array.Empty<string>();
            return false;
        }

        var matches = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!entry.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = dirPrefixForInsert + entry.Name;
            if (entry.IsDirectory)
            {
                candidate += "\\";
            }

            matches.Add(candidate);
        }

        if (matches.Count == 0)
        {
            candidates = Array.Empty<string>();
            return false;
        }

        candidates = matches;
        return true;
    }

    private static List<(string Name, bool IsDirectory)> EnumerateEntries(string directory)
    {
        var dirs = new List<string>();
        var files = new List<string>();

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name))
                {
                    dirs.Add(name);
                }
            }
        }
        catch
        {
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(name))
                {
                    files.Add(name);
                }
            }
        }
        catch
        {
        }

        dirs.Sort(StringComparer.OrdinalIgnoreCase);
        files.Sort(StringComparer.OrdinalIgnoreCase);

        var list = new List<(string Name, bool IsDirectory)>(dirs.Count + files.Count);
        for (var i = 0; i < dirs.Count; i++)
        {
            list.Add((dirs[i], true));
        }

        for (var i = 0; i < files.Count; i++)
        {
            list.Add((files[i], false));
        }

        return list;
    }

    private static bool TryGetDirectoryAndNamePrefix(
        string rawPrefix,
        out string baseDirectory,
        out string dirPrefixForInsert,
        out string namePrefix)
    {
        baseDirectory = string.Empty;
        dirPrefixForInsert = string.Empty;
        namePrefix = string.Empty;

        rawPrefix ??= string.Empty;
        var prefix = rawPrefix.Replace('/', '\\');

        var cwd = string.Empty;
        try
        {
            cwd = Directory.GetCurrentDirectory();
        }
        catch
        {
        }

        var home = string.Empty;
        try
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        catch
        {
        }

        if (!string.IsNullOrEmpty(home) && (prefix == "~" || prefix.StartsWith("~\\", StringComparison.Ordinal)))
        {
            var remainder = prefix.Length == 1 ? string.Empty : prefix.Substring(2);
            var lastSep = remainder.LastIndexOf('\\');
            var dirPart = lastSep >= 0 ? remainder.Substring(0, lastSep + 1) : string.Empty;
            namePrefix = lastSep >= 0 ? remainder.Substring(lastSep + 1) : remainder;

            try
            {
                baseDirectory = Path.GetFullPath(Path.Combine(home, dirPart));
            }
            catch
            {
                return false;
            }

            dirPrefixForInsert = "~\\" + dirPart;
            return true;
        }

        var lastSlash = prefix.LastIndexOf('\\');
        var dirPrefix = lastSlash >= 0 ? prefix.Substring(0, lastSlash + 1) : string.Empty;
        namePrefix = lastSlash >= 0 ? prefix.Substring(lastSlash + 1) : prefix;

        if (string.IsNullOrEmpty(dirPrefix))
        {
            baseDirectory = string.IsNullOrEmpty(cwd) ? string.Empty : Path.GetFullPath(cwd);
            dirPrefixForInsert = string.Empty;
            return !string.IsNullOrEmpty(baseDirectory);
        }

        // "\foo" rooted at the current drive.
        if (dirPrefix.Length > 0 && (dirPrefix[0] == '\\') && !(dirPrefix.Length >= 2 && dirPrefix[1] == '\\'))
        {
            var root = string.IsNullOrEmpty(cwd) ? "\\" : (Path.GetPathRoot(cwd) ?? "\\");
            var trimmed = dirPrefix.TrimStart('\\');
            baseDirectory = Path.GetFullPath(Path.Combine(root, trimmed));
            dirPrefixForInsert = "\\" + trimmed;
            return true;
        }

        // Absolute.
        if (Path.IsPathRooted(dirPrefix))
        {
            try
            {
                baseDirectory = Path.GetFullPath(dirPrefix);
                dirPrefixForInsert = dirPrefix;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Relative.
        try
        {
            baseDirectory = string.IsNullOrEmpty(cwd) ? string.Empty : Path.GetFullPath(Path.Combine(cwd, dirPrefix));
            dirPrefixForInsert = dirPrefix;
            return !string.IsNullOrEmpty(baseDirectory);
        }
        catch
        {
            return false;
        }
    }
}
