using Cycon.Runtime.Events;
using Cycon.Runtime.Jobs;

namespace Cycon.Runtime.Runtime;

public interface IJobRuntime
{
    JobId AllocateJobId();
    IEventSink CreateEventSink(JobId jobId);
    void StartJob(IJob job);
}

