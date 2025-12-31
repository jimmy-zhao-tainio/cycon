using System;

namespace Cycon.Core.Transcript;

public interface IRunnableBlock
{
    BlockRunState State { get; }
    void Tick(TimeSpan dt);
}

