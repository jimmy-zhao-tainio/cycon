using System.IO;

namespace Cycon.Host;

public static class AssetLoader
{
    public static string ResolveFontPath(string basePath, string fileName)
    {
        return Path.Combine(basePath, fileName);
    }
}
