namespace Cycon.Core.Transcript;

public interface IStoppableBlock
{
    bool CanStop { get; }
    void RequestStop(StopLevel level);
}

