using System;
using System.Collections.Generic;
using System.Numerics;
using Cycon.Core.Transcript;

namespace Extensions.Deconstruction.Blocks;

public sealed partial class StlBlock
{
    private bool _edgeStatsComputed;
    private bool _edgeStatsLogged;
    private int _degenerateTriangleCount;
    private int _weldedVertexCount;
    private int _boundaryEdgeCount;
    private int _nonManifoldEdgeCount;
    private int _worstEdgeUse;
    private float _weldEpsilon;

    private float[]? _boundaryEdgeVertexData;
    private int _boundaryEdgeVertexCount;
    private float[]? _nonManifoldEdgeVertexData;
    private int _nonManifoldEdgeVertexCount;

    public IReadOnlyList<string> GetDiagnosticsLines()
    {
        var b = MeshBounds;
        var ext = b.Max - b.Min;

        var tri = TriangleCount;
        var v = VertexCount;

        var sz = $"sz {ext.X:0.####}x{ext.Y:0.####}x{ext.Z:0.####}";

        if (!_edgeStatsComputed)
        {
            return new[]
            {
                $"T {tri}",
                $"V {v}",
                sz,
                "b ?",
                "n ?",
                "m ?"
            };
        }

        var vLine = _weldedVertexCount > 0 ? $"V {v} (w{_weldedVertexCount})" : $"V {v}";
        var bLine = $"b {_boundaryEdgeCount}";
        var nLine = $"n {_nonManifoldEdgeCount}";
        var mLine = $"m {_worstEdgeUse}";
        return new[]
        {
            $"T {tri}",
            vLine,
            sz,
            bLine,
            nLine,
            mLine
        };
    }

    private void EnsureEdgeStatsComputed()
    {
        if (_edgeStatsComputed)
        {
            return;
        }

        var b = MeshBounds;
        var diag = (b.Max - b.Min).Length();
        _weldEpsilon = MathF.Max(diag * 1e-6f, 1e-9f);

        var weldMap = new Dictionary<QuantizedPosKey, int>(capacity: Math.Min(VertexCount, 1_000_000));
        var weldedPositions = new List<Vector3>(capacity: Math.Min(VertexCount, 1_000_000));
        var edgeUse = new Dictionary<ulong, int>(capacity: Math.Min(TriangleCount * 2, 2_000_000));

        var floats = VertexData;
        const int floatsPerVertex = 6;
        var triCount = TriangleCount;

        var diag2 = diag * diag;
        var area2Threshold = MathF.Max(1e-30f, diag2 * diag2 * 1e-24f);
        var degenerate = 0;
        var worstUse = 0;

        for (var tri = 0; tri < triCount; tri++)
        {
            var i0 = checked(tri * 3 * floatsPerVertex);
            var v0 = new Vector3(floats[i0], floats[i0 + 1], floats[i0 + 2]);
            var v1 = new Vector3(floats[i0 + 6], floats[i0 + 7], floats[i0 + 8]);
            var v2 = new Vector3(floats[i0 + 12], floats[i0 + 13], floats[i0 + 14]);

            var e01 = v1 - v0;
            var e02 = v2 - v0;
            var area2 = Vector3.Cross(e01, e02).LengthSquared();
            if (area2 <= area2Threshold)
            {
                degenerate++;
                continue;
            }

            var a = GetWeldedId(weldMap, weldedPositions, v0, _weldEpsilon);
            var b0 = GetWeldedId(weldMap, weldedPositions, v1, _weldEpsilon);
            var c = GetWeldedId(weldMap, weldedPositions, v2, _weldEpsilon);

            worstUse = Math.Max(worstUse, IncrementEdge(edgeUse, a, b0));
            worstUse = Math.Max(worstUse, IncrementEdge(edgeUse, b0, c));
            worstUse = Math.Max(worstUse, IncrementEdge(edgeUse, c, a));
        }

        _degenerateTriangleCount = degenerate;
        _weldedVertexCount = weldedPositions.Count;
        _worstEdgeUse = worstUse;

        // Extract boundary/non-manifold segments.
        var boundaryFloats = new List<float>();
        var nonManifoldFloats = new List<float>();
        var boundaryEdges = 0;
        var nonManifoldEdges = 0;

        foreach (var kvp in edgeUse)
        {
            var count = kvp.Value;
            if (count == 1)
            {
                boundaryEdges++;
                AppendEdge(boundaryFloats, kvp.Key, weldedPositions);
            }
            else if (count >= 3)
            {
                nonManifoldEdges++;
                AppendEdge(nonManifoldFloats, kvp.Key, weldedPositions);
            }
        }

        _boundaryEdgeCount = boundaryEdges;
        _nonManifoldEdgeCount = nonManifoldEdges;

        _boundaryEdgeVertexData = boundaryFloats.Count == 0 ? null : boundaryFloats.ToArray();
        _boundaryEdgeVertexCount = boundaryFloats.Count / floatsPerVertex;
        _nonManifoldEdgeVertexData = nonManifoldFloats.Count == 0 ? null : nonManifoldFloats.ToArray();
        _nonManifoldEdgeVertexCount = nonManifoldFloats.Count / floatsPerVertex;

        _edgeStatsComputed = true;

        if (!_edgeStatsLogged)
        {
            _edgeStatsLogged = true;
            Console.WriteLine($"[STL-EDGE] {FilePath}: eps={_weldEpsilon:0.########} boundaryEdges={_boundaryEdgeCount} nonManifoldEdges={_nonManifoldEdgeCount} worstUse={_worstEdgeUse} degenerateTris={_degenerateTriangleCount} weldedVerts={_weldedVertexCount}");
        }
    }

    private static int GetWeldedId(Dictionary<QuantizedPosKey, int> map, List<Vector3> positions, Vector3 pos, float eps)
    {
        var key = QuantizedPosKey.From(pos, eps);
        if (map.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var id = positions.Count;
        positions.Add(pos);
        map[key] = id;
        return id;
    }

    private static int IncrementEdge(Dictionary<ulong, int> edgeUse, int a, int b)
    {
        if (a == b)
        {
            return 0;
        }

        var min = a < b ? a : b;
        var max = a < b ? b : a;
        var key = ((ulong)(uint)min << 32) | (uint)max;
        if (edgeUse.TryGetValue(key, out var count))
        {
            count++;
            edgeUse[key] = count;
            return count;
        }

        edgeUse[key] = 1;
        return 1;
    }

    private static void AppendEdge(List<float> floats, ulong key, List<Vector3> positions)
    {
        var a = (int)(key >> 32);
        var b = (int)(key & 0xFFFFFFFF);
        var pa = positions[a];
        var pb = positions[b];
        AppendVertex(floats, pa);
        AppendVertex(floats, pb);
    }

    private static void AppendVertex(List<float> floats, Vector3 p)
    {
        // (x,y,z,nx,ny,nz) with a dummy normal for unlit line drawing.
        floats.Add(p.X);
        floats.Add(p.Y);
        floats.Add(p.Z);
        floats.Add(0f);
        floats.Add(0f);
        floats.Add(0f);
    }

    private readonly record struct QuantizedPosKey(long X, long Y, long Z)
    {
        public static QuantizedPosKey From(Vector3 p, float eps)
        {
            var inv = 1.0 / eps;
            var qx = (long)Math.Round(p.X * inv);
            var qy = (long)Math.Round(p.Y * inv);
            var qz = (long)Math.Round(p.Z * inv);
            return new QuantizedPosKey(qx, qy, qz);
        }
    }

    private int GetBoundaryEdgeMeshId() =>
        unchecked((int)(0x40000000u ^ (uint)Id.Value ^ 0x1u));

    private int GetNonManifoldEdgeMeshId() =>
        unchecked((int)(0x40000000u ^ (uint)Id.Value ^ 0x2u));
}
