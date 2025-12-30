using System;

namespace Cycon.Backends.Abstractions.Timing;

public sealed class FrameTimer
{
    public TimeSpan DeltaTime { get; private set; } = TimeSpan.Zero;

    public void Tick(TimeSpan deltaTime) => DeltaTime = deltaTime;
}
