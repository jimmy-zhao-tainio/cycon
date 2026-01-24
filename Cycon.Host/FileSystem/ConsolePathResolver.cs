using System;
using System.Collections.Generic;
using System.IO;

namespace Cycon.Host.FileSystem;

internal static class ConsolePathResolver
{
    public static string ResolvePath(
        string currentDirectory,
        IReadOnlyDictionary<char, string> lastDirectoryPerDrive,
        string rawPath)
    {
        currentDirectory = string.IsNullOrWhiteSpace(currentDirectory)
            ? Directory.GetCurrentDirectory()
            : currentDirectory;

        rawPath ??= string.Empty;
        rawPath = rawPath.Trim();

        if (rawPath.Length == 0)
        {
            return Path.GetFullPath(currentDirectory);
        }

        // Treat "\foo" as rooted at the current drive.
        if (IsRootedWithoutDrive(rawPath))
        {
            var root = Path.GetPathRoot(currentDirectory) ?? currentDirectory;
            var trimmed = rawPath.TrimStart('\\', '/');
            var combined = trimmed.Length == 0 ? root : Path.Combine(root, trimmed);
            return Path.GetFullPath(combined);
        }

        // Switch drive: "D:" -> last directory on D if known, otherwise "D:\".
        if (IsDriveOnly(rawPath, out var driveLetter))
        {
            if (lastDirectoryPerDrive.TryGetValue(char.ToUpperInvariant(driveLetter), out var lastDir) &&
                !string.IsNullOrWhiteSpace(lastDir))
            {
                return Path.GetFullPath(lastDir);
            }

            return Path.GetFullPath(char.ToUpperInvariant(driveLetter) + @":\");
        }

        // Drive-relative: "D:foo" -> relative to last directory on D if known, otherwise D:\.
        if (IsDriveRelative(rawPath, out driveLetter, out var driveRelativeRemainder))
        {
            var baseDir = lastDirectoryPerDrive.TryGetValue(char.ToUpperInvariant(driveLetter), out var lastDir) &&
                          !string.IsNullOrWhiteSpace(lastDir)
                ? lastDir
                : char.ToUpperInvariant(driveLetter) + @":\";

            return Path.GetFullPath(Path.Combine(baseDir, driveRelativeRemainder));
        }

        // Absolute with drive.
        if (Path.IsPathRooted(rawPath))
        {
            return Path.GetFullPath(rawPath);
        }

        // Relative.
        return Path.GetFullPath(Path.Combine(currentDirectory, rawPath));
    }

    private static bool IsDriveOnly(string path, out char driveLetter)
    {
        driveLetter = default;
        if (path.Length != 2 || path[1] != ':')
        {
            return false;
        }

        var ch = path[0];
        if (!char.IsLetter(ch))
        {
            return false;
        }

        driveLetter = ch;
        return true;
    }

    private static bool IsDriveRelative(string path, out char driveLetter, out string remainder)
    {
        driveLetter = default;
        remainder = string.Empty;

        if (path.Length < 3 || path[1] != ':' || !char.IsLetter(path[0]))
        {
            return false;
        }

        if (path[2] == '\\' || path[2] == '/')
        {
            return false;
        }

        driveLetter = path[0];
        remainder = path.Substring(2);
        return true;
    }

    private static bool IsRootedWithoutDrive(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // UNC paths start with \\ and should be treated as rooted already.
        if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
        {
            return false;
        }

        return path[0] == '\\' || path[0] == '/';
    }
}

