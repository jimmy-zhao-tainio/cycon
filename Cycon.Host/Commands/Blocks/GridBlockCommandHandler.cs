using System;
using System.Collections.Generic;
using System.IO;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;

namespace Cycon.Host.Commands.Blocks;

public sealed class GridBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "grid",
        Summary: "Shows folder contents as a thumbnail grid (native block).",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (ctx is not IFileCommandContext fs)
        {
            ctx.InsertTextAfterCommandEcho("File system support is unavailable.", ConsoleTextStream.System);
            return true;
        }

        if (!TryParseArgs(request.Args, out var rawPath, out var sizePx, out var usageError))
        {
            ctx.InsertTextAfterCommandEcho(usageError, ConsoleTextStream.System);
            return true;
        }

        var resolved = string.Empty;
        try
        {
            resolved = fs.ResolvePath(rawPath);
        }
        catch (Exception ex)
        {
            ctx.InsertTextAfterCommandEcho($"Invalid path: {ex.Message}", ConsoleTextStream.System);
            return true;
        }

        var dirPath = resolved;
        if (!fs.FileSystem.DirectoryExists(dirPath) && fs.FileSystem.FileExists(dirPath))
        {
            try
            {
                dirPath = Path.GetDirectoryName(dirPath) ?? dirPath;
            }
            catch
            {
            }
        }

        if (!fs.FileSystem.DirectoryExists(dirPath))
        {
            ctx.InsertTextAfterCommandEcho($"Directory not found: {dirPath}", ConsoleTextStream.System);
            return true;
        }

        var entries = new List<FileSystemEntry>();
        foreach (var entry in fs.FileSystem.Enumerate(dirPath))
        {
            entries.Add(entry);
        }

        entries.Sort(static (a, b) =>
        {
            var byDir = b.IsDirectory.CompareTo(a.IsDirectory);
            return byDir != 0 ? byDir : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });

        var id = ctx.AllocateBlockId();
        ctx.InsertBlockAfterCommandEcho(new ThumbnailGridBlock(id, dirPath, entries, sizePx));
        return true;
    }

    private static bool TryParseArgs(IReadOnlyList<string> args, out string path, out int sizePx, out string usageError)
    {
        path = ".";
        sizePx = 96;
        usageError = "Usage: grid [path] [-s <sizePx>]";

        string? rawPath = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "-s" && i + 1 < args.Count)
            {
                if (!int.TryParse(args[i + 1], out var parsed))
                {
                    usageError = "Invalid size. Usage: grid [path] [-s <sizePx>]";
                    return false;
                }

                sizePx = Math.Clamp(parsed, 16, 512);
                i++;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                usageError = $"Unknown option: {arg}. Usage: grid [path] [-s <sizePx>]";
                return false;
            }

            if (rawPath is not null)
            {
                usageError = $"Unexpected extra arg: {arg}. Usage: grid [path] [-s <sizePx>]";
                return false;
            }

            rawPath = arg;
        }

        if (rawPath is not null)
        {
            path = rawPath;
        }

        return true;
    }
}

