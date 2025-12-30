using Cycon.Host.Input;

namespace Cycon.Host.Interaction;

public abstract record InputEvent
{
    public sealed record Text(char Ch) : InputEvent;

    public sealed record KeyDown(HostKey Key, HostKeyModifiers Mods) : InputEvent;

    public sealed record KeyUp(HostKey Key, HostKeyModifiers Mods) : InputEvent;

    public sealed record MouseDown(int X, int Y, MouseButton Button, HostKeyModifiers Mods) : InputEvent;

    public sealed record MouseMove(int X, int Y, HostKeyModifiers Mods) : InputEvent;

    public sealed record MouseUp(int X, int Y, MouseButton Button, HostKeyModifiers Mods) : InputEvent;

    public sealed record MouseWheel(int X, int Y, int Delta, HostKeyModifiers Mods) : InputEvent;
}

public enum MouseButton
{
    Left,
    Right,
    Middle
}

