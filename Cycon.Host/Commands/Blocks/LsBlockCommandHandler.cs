using System;
using System.Collections.Generic;
using System.IO;
using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Commands;

namespace Cycon.Host.Commands.Blocks;

public sealed class LsBlockCommandHandler : IBlockCommandHandler
{
    public CommandSpec Spec { get; } = new(
        Name: "ls",
        Summary: "Lists directory entries (native block).",
        Aliases: new[] { "dir" },
        Capabilities: CommandCapabilities.None);

    public bool TryExecute(CommandRequest request, IBlockCommandContext ctx)
    {
        if (ctx is not IFileCommandContext fs)
        {
            ctx.InsertTextAfterCommandEcho("File system support is unavailable.", ConsoleTextStream.System);
            return true;
        }

        var rawTarget = request.Args.Count == 0
            ? "."
            : (request.Args.Count == 1 ? request.Args[0] : string.Join(" ", request.Args));

        string fullTarget;
        try
        {
            fullTarget = fs.ResolvePath(rawTarget);
        }
        catch (Exception ex)
        {
            ctx.InsertTextAfterCommandEcho($"Invalid path: {ex.Message}", ConsoleTextStream.System);
            return true;
        }

        if (fs.FileSystem.FileExists(fullTarget))
        {
            var name = Path.GetFileName(fullTarget);
            if (string.IsNullOrEmpty(name))
            {
                name = fullTarget;
            }

            EmitListing(ctx, new[] { new FileSystemEntry(name, fullTarget, IsDirectory: false) });
            return true;
        }

        if (!fs.FileSystem.DirectoryExists(fullTarget))
        {
            ctx.InsertTextAfterCommandEcho($"Directory not found: {fullTarget}", ConsoleTextStream.System);
            return true;
        }

        string? parentDir = null;
        try
        {
            parentDir = Directory.GetParent(fullTarget)?.FullName;
        }
        catch
        {
            parentDir = null;
        }

        var entries = new List<FileSystemEntry>();
        foreach (var entry in fs.FileSystem.Enumerate(fullTarget))
        {
            entries.Add(entry);
        }

        entries.Sort(static (a, b) =>
        {
            var byDir = b.IsDirectory.CompareTo(a.IsDirectory);
            return byDir != 0 ? byDir : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });

        if (!string.IsNullOrEmpty(parentDir))
        {
            entries.Insert(0, new FileSystemEntry("..", parentDir, IsDirectory: true));
        }

        EmitListing(ctx, entries);
        return true;
    }

    private static void EmitListing(IBlockCommandContext ctx, IReadOnlyList<FileSystemEntry> entries)
    {
        var text = new System.Text.StringBuilder();
        var spans = new List<RichTextActionSpan>(entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var displayName = entry.Name == ".."
                ? ".."
                : (entry.IsDirectory ? entry.Name + "\\" : entry.Name);

            var start = text.Length;
            text.Append(displayName);
            spans.Add(new RichTextActionSpan(start, displayName.Length, BuildCommand(entry)));
            text.AppendLine();
        }

        if (text.Length == 0)
        {
            ctx.InsertTextAfterCommandEcho(string.Empty, ConsoleTextStream.Stdout);
            return;
        }

        var id = ctx.AllocateBlockId();
        ctx.InsertBlockAfterCommandEcho(new RichTextBlock(id, text.ToString(), ConsoleTextStream.Stdout, spans));
    }

    private static string BuildCommand(in FileSystemEntry entry)
    {
        var quoted = CommandLineQuote.Quote(entry.FullPath);
        return entry.IsDirectory ? $"cd {quoted}" : $"view {quoted}";
    }
}
