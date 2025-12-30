namespace Cycon.Host.Input;

public readonly record struct HostKeyEvent(HostKey Key, HostKeyModifiers Mods, bool IsDown);

public readonly record struct HostTextInputEvent(char Ch);

public enum HostMouseEventKind
{
    Move,
    Down,
    Up,
    Wheel
}

[System.Flags]
public enum HostMouseButtons
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4
}

public readonly record struct HostMouseEvent(
    HostMouseEventKind Kind,
    int X,
    int Y,
    HostMouseButtons Buttons,
    HostKeyModifiers Mods,
    int WheelDelta);

