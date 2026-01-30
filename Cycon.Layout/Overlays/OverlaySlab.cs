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
    bool Enabled = true);

public sealed class OverlaySlabFrame
{
    public OverlaySlabFrame(
        PxRect outerRectPx,
        PxRect contentRectPx,
        bool isModal,
        string title,
        IReadOnlyList<string> lines,
        IReadOnlyList<UIAction> actions)
    {
        OuterRectPx = outerRectPx;
        ContentRectPx = contentRectPx;
        IsModal = isModal;
        Title = title ?? string.Empty;
        Lines = lines ?? new List<string>();
        Actions = actions ?? new List<UIAction>();
    }

    public PxRect OuterRectPx { get; }
    public PxRect ContentRectPx { get; }
    public bool IsModal { get; }
    public string Title { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<UIAction> Actions { get; }
}
