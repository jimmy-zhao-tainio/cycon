using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Host.Commands.Blocks;
using Extensions.Math;

namespace Cycon.Host.Tests.Commands;

public sealed class HelpCommandTests
{
    [Fact]
    public void Help_ListsCoreCommandsAndExtensions()
    {
        var registry = new BlockCommandRegistry();
        registry.RegisterCore(new HelpBlockCommandHandler(registry));
        registry.RegisterCore(new ClearBlockCommandHandler());
        MathExtensionRegistration.Register(registry);

        var ctx = new FakeContext();
        Assert.True(registry.TryExecute(CommandLineParser.Parse("help")!, ctx));

        Assert.Contains(ctx.Inserted, x => x.Text.Equals("Commands", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ctx.Inserted, x => x.Text.Contains("help", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ctx.Inserted, x => x.Text.Contains("clear", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ctx.Inserted, x => x.Text.Equals("Extensions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ctx.Inserted, x => x.Text.Contains("math", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Help_TopicDelegatesToExtensionHelpProvider()
    {
        var registry = new BlockCommandRegistry();
        registry.RegisterCore(new HelpBlockCommandHandler(registry));
        MathExtensionRegistration.Register(registry);

        var ctx = new FakeContext();
        Assert.True(registry.TryExecute(CommandLineParser.Parse("help math")!, ctx));

        Assert.Contains(ctx.Inserted, x => x.Text.Contains("Math (inline)", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Help_UnknownTopicPrintsFriendlyError()
    {
        var registry = new BlockCommandRegistry();
        registry.RegisterCore(new HelpBlockCommandHandler(registry));

        var ctx = new FakeContext();
        Assert.True(registry.TryExecute(CommandLineParser.Parse("help unknown")!, ctx));

        Assert.Contains(ctx.Inserted, x => x.Text.Contains("Unknown help topic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Help_DoesNotListUnregisteredExtensions()
    {
        var registry = new BlockCommandRegistry();
        registry.RegisterCore(new HelpBlockCommandHandler(registry));
        registry.RegisterCore(new ClearBlockCommandHandler());

        var ctx = new FakeContext();
        Assert.True(registry.TryExecute(CommandLineParser.Parse("help")!, ctx));

        Assert.DoesNotContain(ctx.Inserted, x => x.Text.Contains("math", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeContext : IBlockCommandContext
    {
        public List<(string Text, ConsoleTextStream Stream)> Inserted { get; } = new();
        public BlockId CommandEchoId { get; } = new(1000);
        private int _nextBlockId;

        public BlockId AllocateBlockId() => new(++_nextBlockId);

        public void InsertTextAfterCommandEcho(string text, ConsoleTextStream stream) => Inserted.Add((text, stream));
        public void InsertBlockAfterCommandEcho(IBlock block) { }
        public void OpenInspect(InspectKind kind, string path, string title, IBlock viewBlock, string receiptLine) { }
        public void AttachIndicator(BlockId activityBlockId) { }
        public void AppendOwnedPrompt(string promptText) { }
        public void ClearTranscript() { }
        public void RequestExit() { }
    }
}
