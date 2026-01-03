using Cycon.Host.Input;

namespace Cycon.Host.Hosting;

internal abstract record PendingEvent
{
    public sealed record Text(char Ch) : PendingEvent;

    public sealed record Key(HostKey KeyCode, HostKeyModifiers Mods, bool IsDown) : PendingEvent;

    public sealed record Mouse(HostMouseEvent Event) : PendingEvent;

    public sealed record FileDrop(string Path) : PendingEvent;
}
