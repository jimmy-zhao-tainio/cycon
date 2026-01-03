using System;
using System.Collections.Generic;
using System.IO;

namespace Extensions.Inspect.Receipt;

public static class InspectReceiptFormatter
{
    // CP437 0xFA is a middle dot on the VGA font atlas.
    private const char Dot = (char)0xFA;
    private static readonly string DotSep = "  " + Dot + "  ";

    public static string FormatSingleLine(InspectReceipt receipt)
    {
        if (receipt is null) throw new ArgumentNullException(nameof(receipt));

        var parts = new List<string>(2 + (receipt.MetaFields?.Count ?? 0))
        {
            $"[{receipt.TypeChip}] {receipt.FileName}"
        };

        if (receipt.MetaFields is not null)
        {
            for (var i = 0; i < receipt.MetaFields.Count; i++)
            {
                var field = receipt.MetaFields[i];
                if (!string.IsNullOrWhiteSpace(field))
                {
                    parts.Add(field.Trim());
                }
            }
        }

        return string.Join(DotSep, parts);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        var kb = (long)Math.Ceiling(bytes / 1024.0);
        if (kb < 0) kb = 0;
        return $"{kb} KB";
    }

    public static string FormatLineCount(int lines) => $"{Math.Max(0, lines)} lines";

    public static InspectReceipt CreateText(FileInfo file, int lineCount, string? typeChipOverride = null)
    {
        var chip = !string.IsNullOrWhiteSpace(typeChipOverride) ? typeChipOverride : InferTextChip(file.Extension);
        return new InspectReceipt(
            TypeChip: chip,
            FileName: file.Name,
            MetaFields: new[] { FormatLineCount(lineCount), FormatBytes(file.Length) });
    }

    public static InspectReceipt CreateStl(FileInfo file, int tris, int verts)
    {
        return new InspectReceipt(
            TypeChip: "STL",
            FileName: file.Name,
            MetaFields: new[] { $"{tris} tris", $"{verts} verts", FormatBytes(file.Length) });
    }

    public static InspectReceipt CreateBinary(FileInfo file)
    {
        return new InspectReceipt(
            TypeChip: "BIN",
            FileName: file.Name,
            MetaFields: new[] { FormatBytes(file.Length) });
    }

    private static string InferTextChip(string ext)
    {
        if (string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase)) return "CS";
        if (string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)) return "TXT";
        if (string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase)) return "MD";
        if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase)) return "JSON";
        if (string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase)) return "XML";
        return "TXT";
    }
}
