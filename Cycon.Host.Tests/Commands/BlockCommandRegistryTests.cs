using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Host.Commands.Blocks;

namespace Cycon.Host.Tests.Commands;

public sealed class BlockCommandRegistryTests
{
    [Fact]
    public void Echo_InsertsStdoutText()
    {
        var registry = new BlockCommandRegistry();
        registry.Register(new EchoBlockCommandHandler());

        var ctx = new FakeContext();
        var request = new CommandRequest("echo", new[] { "hi" }, "echo hi");

        var handled = registry.TryExecute(request, ctx);

        Assert.True(handled);
        Assert.Single(ctx.Inserted);
        Assert.Equal(("hi", ConsoleTextStream.Stdout), ctx.Inserted[0]);
    }

    [Fact]
    public void Ask_AppendsOwnedPrompt()
    {
        var registry = new BlockCommandRegistry();
        registry.Register(new AskBlockCommandHandler());

        var ctx = new FakeContext();
        var request = new CommandRequest("ask", new[] { "hello" }, "ask hello");

        var handled = registry.TryExecute(request, ctx);

        Assert.True(handled);
        Assert.Single(ctx.OwnedPrompts);
        Assert.Equal("hello ", ctx.OwnedPrompts[0]);
    }

    [Fact]
    public void Clear_RequiresNoArgs()
    {
        var registry = new BlockCommandRegistry();
        registry.Register(new ClearBlockCommandHandler());

        var ctx = new FakeContext();
        Assert.True(registry.TryExecute(new CommandRequest("clear", Array.Empty<string>(), "clear"), ctx));
        Assert.Equal(1, ctx.ClearCount);

        Assert.False(registry.TryExecute(new CommandRequest("clear", new[] { "x" }, "clear x"), ctx));
        Assert.Equal(1, ctx.ClearCount);
    }

    [Fact]
    public void Exit_RequiresNoArgs()
    {
        var registry = new BlockCommandRegistry();
        registry.Register(new ExitBlockCommandHandler());

        var ctx = new FakeContext();
        Assert.True(registry.TryExecute(new CommandRequest("exit", Array.Empty<string>(), "exit"), ctx));
        Assert.Equal(1, ctx.ExitCount);

        Assert.False(registry.TryExecute(new CommandRequest("exit", new[] { "now" }, "exit now"), ctx));
        Assert.Equal(1, ctx.ExitCount);
    }

    private sealed class FakeContext : IBlockCommandContext
    {
        public List<(string Text, ConsoleTextStream Stream)> Inserted { get; } = new();
        public List<string> OwnedPrompts { get; } = new();
        public int ClearCount { get; private set; }
        public int ExitCount { get; private set; }

        public void InsertTextAfterCommandEcho(string text, ConsoleTextStream stream) => Inserted.Add((text, stream));
        public void AppendOwnedPrompt(string promptText) => OwnedPrompts.Add(promptText);
        public void ClearTranscript() => ClearCount++;
        public void RequestExit() => ExitCount++;
    }
}

