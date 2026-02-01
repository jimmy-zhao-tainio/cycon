using System.Collections.Generic;
using Cycon.Layout.HitTesting;
using Cycon.Layout.Scrolling;

namespace Cycon.Layout.Overlays;

public sealed class OverlaySlab
{
    public OverlaySlab(
        string title,
        bool isModal,
        bool closeOnOutsideClick,
        IReadOnlyList<string> lines,
        IReadOnlyList<OverlaySlabActionSpec> actions)
    {
        Title = title ?? string.Empty;
        IsModal = isModal;
        CloseOnOutsideClick = closeOnOutsideClick;
        Lines = lines ?? new List<string>();
        Actions = actions ?? new List<OverlaySlabActionSpec>();
    }

    public string Title { get; }
    public bool IsModal { get; }
    public bool CloseOnOutsideClick { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<OverlaySlabActionSpec> Actions { get; }
}

public enum OverlaySlabActionArea
{
    Header = 0,
    Content = 1,
    Footer = 2,
}

public enum OverlaySlabActionAlign
{
    Left = 0,
    Right = 1,
}

public readonly record struct OverlaySlabActionSpec(
    UIActionId Id,
    UIActionKind Kind,
    OverlaySlabActionArea Area,
    OverlaySlabActionAlign Align,
    int RowIndex,
    int ColIndex,
    string Label,
    string? CommandText = null,
    bool Enabled = true,
    int MinWidthCols = 0);

public sealed class OverlaySlabFrame
{
    public OverlaySlabFrame(
        PxRect outerRectPx,
        PxRect contentRectPx,
        bool isModal,
        string title,
        IReadOnlyList<string> lines,
        IReadOnlyList<UIAction> actions,
        OverlayTextInputFrame? textInput = null)
    {
        OuterRectPx = outerRectPx;
        ContentRectPx = contentRectPx;
        IsModal = isModal;
        Title = title ?? string.Empty;
        Lines = lines ?? new List<string>();
        Actions = actions ?? new List<UIAction>();
        TextInput = textInput;
    }

    public PxRect OuterRectPx { get; }
    public PxRect ContentRectPx { get; }
    public bool IsModal { get; }
    public string Title { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<UIAction> Actions { get; }
    public OverlayTextInputFrame? TextInput { get; }
}

public sealed class OverlayTextInputFrame
{
    public OverlayTextInputFrame(
        UIActionId id,
        PxRect outerRectPx,
        string text,
        int caretIndex,
        int? selectionAnchorIndex,
        int scrollXPx,
        byte caretAlpha)
    {
        Id = id;
        OuterRectPx = outerRectPx;
        Text = text ?? string.Empty;
        CaretIndex = caretIndex;
        SelectionAnchorIndex = selectionAnchorIndex;
        ScrollXPx = scrollXPx;
        CaretAlpha = caretAlpha;
    }

    public UIActionId Id { get; }
    public PxRect OuterRectPx { get; }
    public string Text { get; }
    public int CaretIndex { get; }
    public int? SelectionAnchorIndex { get; }
    public int ScrollXPx { get; }
    public byte CaretAlpha { get; }
}
