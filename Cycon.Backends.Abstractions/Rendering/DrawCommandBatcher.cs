using System.Collections.Generic;

namespace Cycon.Backends.Abstractions.Rendering;

public enum DrawCommandBatchKind
{
    Glyph,
    Quad
}

public readonly record struct DrawCommandBatch(DrawCommandBatchKind Kind, int StartIndex, int Count);

public static class DrawCommandBatcher
{
    public static IReadOnlyList<DrawCommandBatch> ComputeBatches(IReadOnlyList<DrawCommand> commands)
    {
        var batches = new List<DrawCommandBatch>();

        DrawCommandBatchKind? currentKind = null;
        var currentStart = 0;

        for (var i = 0; i < commands.Count; i++)
        {
            var kind = GetKind(commands[i]);
            if (kind is null)
            {
                Flush(batches, currentKind, currentStart, i);
                currentKind = null;
                continue;
            }

            if (currentKind is null)
            {
                currentKind = kind;
                currentStart = i;
                continue;
            }

            if (currentKind.Value != kind.Value)
            {
                Flush(batches, currentKind, currentStart, i);
                currentKind = kind;
                currentStart = i;
            }
        }

        Flush(batches, currentKind, currentStart, commands.Count);
        return batches;
    }

    private static DrawCommandBatchKind? GetKind(DrawCommand command)
    {
        return command switch
        {
            DrawGlyphRun => DrawCommandBatchKind.Glyph,
            DrawQuad => DrawCommandBatchKind.Quad,
            DrawTriangles => DrawCommandBatchKind.Quad,
            DrawVignetteQuad => DrawCommandBatchKind.Quad,
            DrawImage2D => null,
            _ => null
        };
    }

    private static void Flush(
        List<DrawCommandBatch> batches,
        DrawCommandBatchKind? kind,
        int startIndex,
        int endIndexExclusive)
    {
        if (kind is null)
        {
            return;
        }

        var count = endIndexExclusive - startIndex;
        if (count <= 0)
        {
            return;
        }

        batches.Add(new DrawCommandBatch(kind.Value, startIndex, count));
    }
}
