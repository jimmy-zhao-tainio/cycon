namespace Cycon.BlockCommands;

public interface IFileCommandContext
{
    string CurrentDirectory { get; }
    string ResolvePath(string path);
    bool TrySetCurrentDirectory(string directory, out string error);
    IFileSystem FileSystem { get; }
}

