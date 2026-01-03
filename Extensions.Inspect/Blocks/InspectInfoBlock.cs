using System;
using System.Collections.Generic;
using System.IO;
using Cycon.Core.Transcript;
using Cycon.Render;

namespace Extensions.Inspect.Blocks;

public sealed class InspectInfoBlock : IBlock, IRenderBlock
{
    private readonly IReadOnlyList<string> _lines;

    public InspectInfoBlock(BlockId id, IReadOnlyList<string> lines)
    {
        Id = id;
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
    }

    public BlockId Id { get; }

    // Rendered via IRenderBlock in fullscreen inspect mode; layout kind is irrelevant.
    public BlockKind Kind => BlockKind.Text;

    public void Render(IRenderCanvas canvas, in BlockRenderContext ctx)
    {
        var rect = ctx.ViewportRectPx;
        canvas.FillRect(rect, ctx.Theme.BackgroundRgba);

        var cellW = Math.Max(1, ctx.TextMetrics.CellWidthPx);
        var cellH = Math.Max(1, ctx.TextMetrics.CellHeightPx);
        var cols = Math.Max(1, rect.Width / cellW);
        var rows = Math.Max(1, rect.Height / cellH);

        var fg = ctx.Theme.ForegroundRgba;
        var maxLines = Math.Min(rows, _lines.Count);
        for (var i = 0; i < maxLines; i++)
        {
            var line = _lines[i] ?? string.Empty;
            var len = Math.Min(line.Length, cols);
            canvas.DrawText(line, 0, len, rect.X, rect.Y + (i * cellH), fg);
        }
    }

    public static InspectInfoBlock FromFile(BlockId id, FileInfo file)
    {
        var lines = new List<string>
        {
            $"Name: {file.Name}",
            $"Size: {file.Length} bytes",
            $"Ext:  {file.Extension}",
            $"Path: {file.FullName}",
        };
        return new InspectInfoBlock(id, lines);
    }
}

