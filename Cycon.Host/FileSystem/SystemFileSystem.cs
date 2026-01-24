using System;
using System.Collections.Generic;
using System.IO;
using Cycon.BlockCommands;

namespace Cycon.Host.FileSystem;

internal sealed class SystemFileSystem : IFileSystem
{
    public bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public IEnumerable<FileSystemEntry> Enumerate(string directory)
    {
        IEnumerable<string> dirs;
        IEnumerable<string> files;

        try
        {
            dirs = Directory.EnumerateDirectories(directory);
        }
        catch
        {
            dirs = Array.Empty<string>();
        }

        try
        {
            files = Directory.EnumerateFiles(directory);
        }
        catch
        {
            files = Array.Empty<string>();
        }

        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            yield return new FileSystemEntry(name, dir, IsDirectory: true);
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            yield return new FileSystemEntry(name, file, IsDirectory: false);
        }
    }

    public string ReadAllText(string path)
    {
        return File.ReadAllText(path);
    }
}

