namespace Cycon.BlockCommands;

public interface IFileCommandContext
{
    string HomeDirectory { get; }
    string CurrentDirectory { get; }
    string ResolvePath(string path);
    bool TrySetCurrentDirectory(string directory, out string error);
    IFileSystem FileSystem { get; }
}
