namespace Cycon.Backends.Abstractions.Input;

public readonly record struct MouseEvent(int X, int Y, MouseButtons Buttons, int ScrollDelta);

[System.Flags]
public enum MouseButtons
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4
}
