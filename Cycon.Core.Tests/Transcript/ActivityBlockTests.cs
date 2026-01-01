using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;

namespace Cycon.Core.Tests.Transcript;

public sealed class ActivityBlockTests
{
    [Fact]
    public void Wait_Completes_AfterDuration()
    {
        var block = new ActivityBlock(
            id: new BlockId(1),
            label: "wait",
            kind: ActivityKind.Wait,
            duration: TimeSpan.FromMilliseconds(100),
            stream: ConsoleTextStream.System);

        Assert.Equal(BlockRunState.Running, block.State);

        block.Tick(TimeSpan.FromMilliseconds(50));
        Assert.Equal(BlockRunState.Running, block.State);

        block.Tick(TimeSpan.FromMilliseconds(60));
        Assert.Equal(BlockRunState.Completed, block.State);
    }

    [Fact]
    public void Progress_Fraction_Increases_AndEndsAtOne()
    {
        var block = new ActivityBlock(
            id: new BlockId(1),
            label: "progress",
            kind: ActivityKind.Progress,
            duration: TimeSpan.FromMilliseconds(100),
            stream: ConsoleTextStream.System);

        var last = -1.0;
        for (var i = 0; i < 5; i++)
        {
            block.Tick(TimeSpan.FromMilliseconds(25));
            var f = block.Progress.Fraction ?? 0;
            Assert.True(f >= last);
            last = f;
        }

        Assert.Equal(BlockRunState.Completed, block.State);
        Assert.Equal(1.0, block.Progress.Fraction);
    }

    [Fact]
    public void RequestStop_Cancels()
    {
        var block = new ActivityBlock(
            id: new BlockId(1),
            label: "wait",
            kind: ActivityKind.Wait,
            duration: TimeSpan.FromSeconds(1),
            stream: ConsoleTextStream.System);

        Assert.True(block.CanStop);
        block.RequestStop(StopLevel.Kill);
        Assert.Equal(BlockRunState.Cancelled, block.State);
        Assert.False(block.CanStop);
    }
}

