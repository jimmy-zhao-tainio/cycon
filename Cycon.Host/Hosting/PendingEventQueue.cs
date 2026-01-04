namespace Cycon.Host.Hosting;

internal sealed class PendingEventQueue
{
    private readonly Queue<PendingEvent> _pendingEvents = new();
    private readonly object _lock = new();

    public void Enqueue(PendingEvent e)
    {
        lock (_lock)
        {
            _pendingEvents.Enqueue(e);
        }
    }

    public List<PendingEvent>? DequeueAll()
    {
        lock (_lock)
        {
            if (_pendingEvents.Count == 0)
            {
                return null;
            }

            var events = new List<PendingEvent>(_pendingEvents.Count);
            while (_pendingEvents.Count > 0)
            {
                events.Add(_pendingEvents.Dequeue());
            }

            return events;
        }
    }
}
