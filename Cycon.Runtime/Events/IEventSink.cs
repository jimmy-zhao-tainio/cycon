using Cycon.Runtime.Jobs;

namespace Cycon.Runtime.Events;

public interface IEventSink
{
    void Publish(JobId jobId, ConsoleEvent e);
}

