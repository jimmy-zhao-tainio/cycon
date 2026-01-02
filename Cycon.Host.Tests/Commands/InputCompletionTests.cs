using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Host.Commands.Input;
using Cycon.Host.Commands.Blocks;
using Extensions.Math;

namespace Cycon.Host.Tests.Commands;

public sealed class InputCompletionTests
{
    [Fact]
    public void TabCompletesSingleCommand()
    {
        var registry = new BlockCommandRegistry();
        registry.RegisterCore(new HelpBlockCommandHandler(registry));
        registry.RegisterCore(new ClearBlockCommandHandler());

        var completion = new InputCompletionController(new CommandCompletionProvider(registry));

        Assert.True(completion.TryHandleTab("he", 2, reverseCycle: false, out var text, out var caret, out var matches));
        Assert.Equal("help", text);
        Assert.Equal(4, caret);
        Assert.Null(matches);
    }

    [Fact]
    public void TabUsesCommonPrefixThenListsThenCycles()
    {
        var registry = new BlockCommandRegistry();
        registry.RegisterCore(new FakeHandler("print"));
        registry.RegisterCore(new FakeHandler("progress"));

        var completion = new InputCompletionController(new CommandCompletionProvider(registry));

        Assert.True(completion.TryHandleTab("p", 1, reverseCycle: false, out var t1, out var c1, out var m1));
        Assert.Equal("pr", t1);
        Assert.Equal(2, c1);
        Assert.Null(m1);

        Assert.True(completion.TryHandleTab(t1, c1, reverseCycle: false, out var t2, out var c2, out var m2));
        Assert.Equal("pr", t2);
        Assert.Equal(2, c2);
        Assert.Equal("Matches: print progress", m2);

        Assert.True(completion.TryHandleTab(t2, c2, reverseCycle: false, out var t3, out var c3, out var m3));
        Assert.Equal("print", t3);
        Assert.Equal(5, c3);
        Assert.Null(m3);

        Assert.True(completion.TryHandleTab(t3, c3, reverseCycle: false, out var t4, out var c4, out var m4));
        Assert.Equal("progress", t4);
        Assert.Equal(8, c4);
        Assert.Null(m4);

        Assert.True(completion.TryHandleTab(t4, c4, reverseCycle: true, out var t5, out var c5, out var m5));
        Assert.Equal("print", t5);
        Assert.Equal(5, c5);
        Assert.Null(m5);
    }

    [Fact]
    public void HelpTargetCompletionIncludesExtensions()
    {
        var registry = new BlockCommandRegistry();
        registry.RegisterCore(new HelpBlockCommandHandler(registry));
        MathExtensionRegistration.Register(registry);

        var completion = new InputCompletionController(new CommandCompletionProvider(registry));

        Assert.True(completion.TryHandleTab("help m", 6, reverseCycle: false, out var text, out var caret, out var matches));
        Assert.Equal("help math", text);
        Assert.Equal(9, caret);
        Assert.Null(matches);
    }

    private sealed class FakeHandler : IBlockCommandHandler
    {
        public FakeHandler(string name)
        {
            Spec = new CommandSpec(name, "fake", Array.Empty<string>(), CommandCapabilities.None);
        }

        public CommandSpec Spec { get; }

        public bool TryExecute(CommandRequest request, IBlockCommandContext ctx) => false;
    }
}

