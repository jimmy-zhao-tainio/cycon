using Cycon.BlockCommands;
using Cycon.Commands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Extensions.Math;

namespace Cycon.Host.Tests.Extensions;

public sealed class MathExtensionTests
{
    [Fact]
    public void MathInactive_DoesNotHandleExpression()
    {
        var registry = new BlockCommandRegistry();
        var ctx = new FakeContext();
        var raw = "1 + 1";
        var request = CommandLineParser.Parse(raw)!;

        var handled = registry.TryExecuteOrFallback(request, raw, ctx);

        Assert.False(handled);
        Assert.Empty(ctx.Inserted);
    }

    [Fact]
    public void MathActive_EvaluatesExpression()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);

        var ctx = new FakeContext();
        var raw = "1 + 1";
        var request = CommandLineParser.Parse(raw)!;

        var handled = registry.TryExecuteOrFallback(request, raw, ctx);

        Assert.True(handled);
        Assert.Single(ctx.Inserted);
        Assert.Equal(("2", ConsoleTextStream.Stdout), ctx.Inserted[0]);
    }

    [Fact]
    public void MathActive_AssignmentIsCaseInsensitiveAndPersists()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("X = 5")!, "X = 5", ctx));
        Assert.Equal(("X = 5", ConsoleTextStream.Stdout), ctx.Inserted[^1]);

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("x * 2")!, "x * 2", ctx));
        Assert.Equal(("10", ConsoleTextStream.Stdout), ctx.Inserted[^1]);
    }

    [Fact]
    public void MathActive_AnsIsUpdated()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("1 + 1")!, "1 + 1", ctx));
        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("ans + 1")!, "ans + 1", ctx));
        Assert.Equal(("3", ConsoleTextStream.Stdout), ctx.Inserted[^1]);
    }

    [Fact]
    public void MathActive_FunctionsWork()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("sin(pi/2)")!, "sin(pi/2)", ctx));
        Assert.Equal(ConsoleTextStream.Stdout, ctx.Inserted[^1].Stream);
        Assert.InRange(double.Parse(ctx.Inserted[^1].Text, System.Globalization.CultureInfo.InvariantCulture), 0.999999999999, 1.000000000001);
    }

    [Fact]
    public void MathActive_PrecedenceAndPower()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("1 + 2 * 3")!, "1 + 2 * 3", ctx));
        Assert.Equal(("7", ConsoleTextStream.Stdout), ctx.Inserted[^1]);

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("(1 + 2) * 3")!, "(1 + 2) * 3", ctx));
        Assert.Equal(("9", ConsoleTextStream.Stdout), ctx.Inserted[^1]);

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("2 ^ 3")!, "2 ^ 3", ctx));
        Assert.Equal(("8", ConsoleTextStream.Stdout), ctx.Inserted[^1]);

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("2^3^2")!, "2^3^2", ctx));
        Assert.Equal(("512", ConsoleTextStream.Stdout), ctx.Inserted[^1]);

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("(2^3)^2")!, "(2^3)^2", ctx));
        Assert.Equal(("64", ConsoleTextStream.Stdout), ctx.Inserted[^1]);

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("2^(3^2)")!, "2^(3^2)", ctx));
        Assert.Equal(("512", ConsoleTextStream.Stdout), ctx.Inserted[^1]);

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("2^3^(1+1)")!, "2^3^(1+1)", ctx));
        Assert.Equal(("512", ConsoleTextStream.Stdout), ctx.Inserted[^1]);
    }

    [Fact]
    public void MathActive_InvalidExpressionReturnsSingleLineError()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("1 +")!, "1 +", ctx));
        Assert.Equal(ConsoleTextStream.Default, ctx.Inserted[^1].Stream);
        Assert.NotEmpty(ctx.Inserted[^1].Text);
    }

    [Fact]
    public void MathActive_ExponentChainOverflowIsNotDivideByZero()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("3^4^5")!, "3^4^5", ctx));
        Assert.Equal(ConsoleTextStream.Default, ctx.Inserted[^1].Stream);
        Assert.Equal("Result out of range.", ctx.Inserted[^1].Text);
    }

    [Fact]
    public void MathActive_DivideByZeroIsReported()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("23 / 0")!, "23 / 0", ctx));
        Assert.Equal(ConsoleTextStream.Default, ctx.Inserted[^1].Stream);
        Assert.Equal("Divide by zero.", ctx.Inserted[^1].Text);
    }

    [Fact]
    public void MathActive_ZeroOverZeroIsInvalidNumeric()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("0 / 0")!, "0 / 0", ctx));
        Assert.Equal(ConsoleTextStream.Default, ctx.Inserted[^1].Stream);
        Assert.Equal("Invalid numeric result.", ctx.Inserted[^1].Text);
    }

    [Fact]
    public void MathActive_SqrtNegativeIsInvalidNumeric()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("sqrt(-1)")!, "sqrt(-1)", ctx));
        Assert.Equal(ConsoleTextStream.Default, ctx.Inserted[^1].Stream);
        Assert.Equal("Invalid numeric result.", ctx.Inserted[^1].Text);
    }

    [Fact]
    public void MathActive_UnknownVariableReturnsSingleLineError()
    {
        var registry = new BlockCommandRegistry();
        MathExtensionRegistration.Register(registry);
        var ctx = new FakeContext();

        Assert.True(registry.TryExecuteOrFallback(CommandLineParser.Parse("zz + 1")!, "zz + 1", ctx));
        Assert.Equal(ConsoleTextStream.Default, ctx.Inserted[^1].Stream);
        Assert.Contains("Unknown variable", ctx.Inserted[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeContext : IBlockCommandContext
    {
        public List<(string Text, ConsoleTextStream Stream)> Inserted { get; } = new();
        public BlockId CommandEchoId { get; } = new(1000);
        private int _nextBlockId;

        public BlockId AllocateBlockId() => new(++_nextBlockId);

        public void InsertTextAfterCommandEcho(string text, ConsoleTextStream stream) => Inserted.Add((text, stream));
        public void InsertBlockAfterCommandEcho(IBlock block) { }
        public void AttachIndicator(BlockId activityBlockId) { }
        public void AppendOwnedPrompt(string promptText) { }
        public void ClearTranscript() { }
        public void RequestExit() { }
    }
}
