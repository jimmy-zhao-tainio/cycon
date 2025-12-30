namespace Cycon.Backends.Abstractions.Input;

public readonly record struct KeyEvent(int KeyCode, bool IsDown, KeyModifiers Modifiers);

[System.Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4
}
