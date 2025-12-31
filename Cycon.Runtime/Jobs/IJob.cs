using System.Threading;
using System.Threading.Tasks;

namespace Cycon.Runtime.Jobs;

public interface IJob
{
    JobId Id { get; }
    string Kind { get; }
    JobState State { get; }

    Task RunAsync(CancellationToken ct);

    Task SendInputAsync(string text, CancellationToken ct);
    Task RequestCancelAsync(CancelLevel level, CancellationToken ct);
}

