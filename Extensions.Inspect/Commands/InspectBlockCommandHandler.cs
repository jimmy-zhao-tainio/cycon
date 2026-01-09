using System;
using System.IO;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Extensions.Inspect.Blocks;
using Extensions.Inspect.Receipt;
using Extensions.Inspect.Stl;
using Extensions.Inspect.Text;

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
        if (!TryParseArgs(request, out var fullPath, out var navMode, out var inline, out var usageError))
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
                var receipt = InspectReceiptFormatter.CreateStl(file, stl.TriangleCount, stl.VertexCount);
                if (inline)
                {
                    ctx.InsertBlockAfterCommandEcho(stl);
                }
                else
                {
                    ctx.OpenInspect(InspectKind.Binary, fullPath, file.Name, stl, InspectReceiptFormatter.FormatSingleLine(receipt));
                }
            }
            catch (Exception ex)
            {
                ctx.InsertTextAfterCommandEcho($"STL load failed: {ex.Message}", ConsoleTextStream.System);
                var fallback = InspectInfoBlock.FromFile(ctx.AllocateBlockId(), file);
                var receipt = InspectReceiptFormatter.CreateBinary(file);
                if (inline)
                {
                    ctx.InsertBlockAfterCommandEcho(fallback);
                }
                else
                {
                    ctx.OpenInspect(InspectKind.Binary, fullPath, file.Name, fallback, InspectReceiptFormatter.FormatSingleLine(receipt));
                }
            }

            return true;
        }

        if (IsImageExtension(file.Extension))
        {
            try
            {
                var blockId = ctx.AllocateBlockId();
                var image = ImageBlock.Load(blockId, fullPath);
                var receipt = InspectReceiptFormatter.CreateBinary(file);
                if (inline)
                {
                    ctx.InsertBlockAfterCommandEcho(image);
                }
                else
                {
                    ctx.OpenInspect(InspectKind.Binary, fullPath, file.Name, image, InspectReceiptFormatter.FormatSingleLine(receipt));
                }
            }
            catch (Exception ex)
            {
                ctx.InsertTextAfterCommandEcho($"Image load failed: {ex.Message}", ConsoleTextStream.System);
                var fallback = InspectInfoBlock.FromFile(ctx.AllocateBlockId(), file);
                var receipt = InspectReceiptFormatter.CreateBinary(file);
                if (inline)
                {
                    ctx.InsertBlockAfterCommandEcho(fallback);
                }
                else
                {
                    ctx.OpenInspect(InspectKind.Binary, fullPath, file.Name, fallback, InspectReceiptFormatter.FormatSingleLine(receipt));
                }
            }

            return true;
        }

        if (TextSniffer.LooksLikeTextFile(fullPath))
        {
            try
            {
                var blockId = ctx.AllocateBlockId();
                var view = new InspectTextBlock(blockId, fullPath);
                var lineCount = view.LineCount;
                var receipt = InspectReceiptFormatter.CreateText(file, lineCount);
                if (inline)
                {
                    ctx.InsertBlockAfterCommandEcho(view);
                }
                else
                {
                    ctx.OpenInspect(InspectKind.Text, fullPath, file.Name, view, InspectReceiptFormatter.FormatSingleLine(receipt));
                }
            }
            catch (Exception ex)
            {
                ctx.InsertTextAfterCommandEcho($"Text load failed: {ex.Message}", ConsoleTextStream.System);
                var fallback = InspectInfoBlock.FromFile(ctx.AllocateBlockId(), file);
                var receipt = InspectReceiptFormatter.CreateText(file, lineCount: 0);
                if (inline)
                {
                    ctx.InsertBlockAfterCommandEcho(fallback);
                }
                else
                {
                    ctx.OpenInspect(InspectKind.Text, fullPath, file.Name, fallback, InspectReceiptFormatter.FormatSingleLine(receipt));
                }
            }

            return true;
        }

        var info = InspectInfoBlock.FromFile(ctx.AllocateBlockId(), file);
        var binaryReceipt = InspectReceiptFormatter.CreateBinary(file);
        if (inline)
        {
            ctx.InsertBlockAfterCommandEcho(info);
        }
        else
        {
            ctx.OpenInspect(InspectKind.Binary, fullPath, file.Name, info, InspectReceiptFormatter.FormatSingleLine(binaryReceipt));
        }
        return true;
    }

    private static bool IsImageExtension(string extension)
    {
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseArgs(
        CommandRequest request,
        out string fullPath,
        out Scene3DNavigationMode navMode,
        out bool inline,
        out string usageError)
    {
        fullPath = string.Empty;
        navMode = Scene3DNavigationMode.FreeFly;
        inline = false;
        usageError = "Usage: inspect <path> [--inline|--fullscreen] [--free-fly|--orbit]";

        string? rawPath = null;
        var inlineRequested = false;
        var fullscreenRequested = false;
        foreach (var arg in request.Args)
        {
            if (arg is "--inline")
            {
                inlineRequested = true;
                continue;
            }

            if (arg is "--fullscreen")
            {
                fullscreenRequested = true;
                continue;
            }

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

        if (inlineRequested && fullscreenRequested)
        {
            usageError = $"Conflicting view mode flags. {usageError}";
            return false;
        }

        inline = inlineRequested && !fullscreenRequested;

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


}
