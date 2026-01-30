namespace Cycon.Layout.HitTesting;

public readonly record struct UIActionState(
    UIActionId? HoveredId,
    UIActionId? PressedId,
    UIActionId? FocusedId)
{
    public static UIActionState Empty => new(null, null, null);
}

