using System;
using System.Runtime.CompilerServices;
using Cycon.Core.Transcript;
using Cycon.Layout.Metrics;
using Cycon.Layout.Scrolling;

namespace Cycon.Layout.HitTesting;

public enum UIActionKind
{
    InsertText = 0,
    ExecuteCommand = 1,
    CloseOverlay = 2,
    NoOp = 3,
}

public readonly record struct UIActionId(ulong Value)
{
    public bool IsEmpty => Value == 0;

    public static UIActionId Empty => default;
}

public readonly record struct UIAction(
    UIActionId Id,
    PxRect RectPx,
    string Label,
    string CommandText,
    bool Enabled,
    UIActionKind Kind,
    BlockId BlockId,
    int CharStart,
    int CharLength);

public static class UIActionFactory
{
    public static UIAction FromSpan(in HitTestActionSpan span, UIActionKind kind = UIActionKind.InsertText, bool enabled = true)
    {
        return new UIAction(
            Id: GetId(span),
            RectPx: span.RectPx,
            Label: string.Empty,
            CommandText: span.CommandText,
            Enabled: enabled,
            Kind: kind,
            BlockId: span.BlockId,
            CharStart: span.CharStart,
            CharLength: span.CharLength);
    }

    public static UIActionId GetId(in HitTestActionSpan span)
    {
        // Deterministic ID (do not use HashCode.Combine which is randomized per process).
        // Include coordinates within the block text to keep stable even when rows/wrapping changes.
        unchecked
        {
            ulong h = 1469598103934665603UL; // FNV-1a offset basis (64-bit)
            h = Fnv1a(h, (uint)span.BlockId.Value);
            h = Fnv1a(h, (uint)span.CharStart);
            h = Fnv1a(h, (uint)span.CharLength);
            h = Fnv1aString(h, span.CommandText);
            if (h == 0)
            {
                h = 1;
            }

            return new UIActionId(h);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Fnv1a(ulong h, uint data)
    {
        h ^= (byte)data;
        h *= 1099511628211UL;
        h ^= (byte)(data >> 8);
        h *= 1099511628211UL;
        h ^= (byte)(data >> 16);
        h *= 1099511628211UL;
        h ^= (byte)(data >> 24);
        h *= 1099511628211UL;
        return h;
    }

    private static ulong Fnv1aString(ulong h, string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return h;
        }

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            h ^= (byte)c;
            h *= 1099511628211UL;
            h ^= (byte)(c >> 8);
            h *= 1099511628211UL;
        }

        return h;
    }
}
