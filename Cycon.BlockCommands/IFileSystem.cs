using System.Collections.Generic;

namespace Cycon.BlockCommands;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IEnumerable<FileSystemEntry> Enumerate(string directory);
    IEnumerable<FileSystemEntry> Enumerate(string directory, string searchPattern);
    string ReadAllText(string path);
}

public readonly record struct FileSystemEntry(string Name, string FullPath, bool IsDirectory);
