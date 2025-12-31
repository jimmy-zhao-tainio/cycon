using Cycon.Commands;

namespace Cycon.Host.Tests.Commands;

public sealed class CommandLineParserTests
{
    [Fact]
    public void Tokenize_Empty_ReturnsEmpty()
    {
        Assert.Empty(CommandLineParser.Tokenize(""));
        Assert.Empty(CommandLineParser.Tokenize("   "));
    }

    [Fact]
    public void Tokenize_WhitespaceSplit_Works()
    {
        var tokens = CommandLineParser.Tokenize("echo  hello   world");
        Assert.Equal(new[] { "echo", "hello", "world" }, tokens);
    }

    [Fact]
    public void Tokenize_Quotes_PreservesSpaces()
    {
        var tokens = CommandLineParser.Tokenize("echo \"hello world\"");
        Assert.Equal(new[] { "echo", "hello world" }, tokens);
    }

    [Fact]
    public void Tokenize_Escapes_Work()
    {
        var tokens = CommandLineParser.Tokenize("echo \"a\\\"b\" c\\\\d");
        Assert.Equal(new[] { "echo", "a\"b", "c\\d" }, tokens);
    }
}

