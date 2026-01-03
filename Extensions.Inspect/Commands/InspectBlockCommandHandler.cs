using System;
using System.IO;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Extensions.Inspect.Blocks;
using Extensions.Inspect.Stl;

namespace Extensions.Inspect.Commands;

public sealed class InspectBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "inspect",
        Summary: "Inspect file and spawn suitable block view.",
        Aliases: Array.Empty<string>(),
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (!TryParseArgs(request, out var fullPath, out var navMode, out var usageError))
        {
            ctx.InsertTextAfterCommandEcho(usageError, ConsoleTextStream.System);
            return true;
        }

        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            ctx.InsertTextAfterCommandEcho($"File not found: {fullPath}", ConsoleTextStream.System);
            return true;
        }

        if (string.Equals(file.Extension, ".stl", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var blockId = ctx.AllocateBlockId();
                var stl = StlLoader.LoadBlock(blockId, fullPath);
                if (stl is IScene3DOrbitBlock orbit)
                {
                    orbit.NavigationMode = navMode;
                }
                ctx.InsertBlockAfterCommandEcho(stl);
            }
            catch (Exception ex)
            {
                ctx.InsertTextAfterCommandEcho($"STL load failed: {ex.Message}", ConsoleTextStream.System);
                InsertInspector(ctx, file);
            }

            return true;
        }

        InsertInspector(ctx, file);
        return true;
    }

    private static bool TryParseArgs(
        CommandRequest request,
        out string fullPath,
        out Scene3DNavigationMode navMode,
        out string usageError)
    {
        fullPath = string.Empty;
        navMode = Scene3DNavigationMode.FreeFly;
        usageError = "Usage: inspect <path> [--free-fly|--orbit]";

        string? rawPath = null;
        foreach (var arg in request.Args)
        {
            if (arg is "--orbit")
            {
                if (navMode != Scene3DNavigationMode.FreeFly)
                {
                    usageError = $"Conflicting mode flag: {arg}. {usageError}";
                    return false;
                }

                navMode = Scene3DNavigationMode.Orbit;
                continue;
            }

            if (arg is "--free-fly")
            {
                if (navMode != Scene3DNavigationMode.FreeFly)
                {
                    usageError = $"Conflicting mode flag: {arg}. {usageError}";
                    return false;
                }

                navMode = Scene3DNavigationMode.FreeFly;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                usageError = $"Unknown option: {arg}. {usageError}";
                return false;
            }

            if (rawPath is not null)
            {
                usageError = $"Unexpected extra arg: {arg}. {usageError}";
                return false;
            }

            rawPath = arg;
        }

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(rawPath);
            return true;
        }
        catch (Exception ex)
        {
            usageError = $"Invalid path: {ex.Message}";
            return false;
        }
    }

    private static void InsertInspector(IBlockCommandContext ctx, FileInfo file)
    {
        ctx.InsertTextAfterCommandEcho($"Name: {file.Name}", ConsoleTextStream.System);
        ctx.InsertTextAfterCommandEcho($"Size: {file.Length} bytes", ConsoleTextStream.System);
        ctx.InsertTextAfterCommandEcho($"Ext:  {file.Extension}", ConsoleTextStream.System);
        ctx.InsertTextAfterCommandEcho($"Path: {file.FullName}", ConsoleTextStream.System);
    }
}
