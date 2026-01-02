using System;
using System.IO;
using Cycon.Host.Commands.Input;

namespace Cycon.Host.Tests.Commands;

public sealed class InputHistoryTests
{
    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "history.txt");
        var history = InputHistory.Load(path, maxEntries: 1000);
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void RecordSubmitted_PersistsAcrossRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "history.txt");

        var history = InputHistory.Load(path, maxEntries: 1000);
        history.RecordSubmitted("echo hi");
        history.RecordSubmitted("echo bye");

        var history2 = InputHistory.Load(path, maxEntries: 1000);
        Assert.Equal(new[] { "echo hi", "echo bye" }, history2.Entries);
    }

    [Fact]
    public void RecordSubmitted_AdjacentDuplicatesAreCompressed()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "history.txt");

        var history = InputHistory.Load(path, maxEntries: 1000);
        history.RecordSubmitted("echo hi");
        history.RecordSubmitted("echo hi");
        history.RecordSubmitted("echo hi");

        Assert.Single(history.Entries);
        Assert.Equal("echo hi", history.Entries[0]);
    }

    [Fact]
    public void RecordSubmitted_NonAdjacentDuplicatesAreAllowed()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "history.txt");

        var history = InputHistory.Load(path, maxEntries: 1000);
        history.RecordSubmitted("a");
        history.RecordSubmitted("b");
        history.RecordSubmitted("a");

        Assert.Equal(new[] { "a", "b", "a" }, history.Entries);
    }

    [Fact]
    public void RecordSubmitted_EmptyIsIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "history.txt");

        var history = InputHistory.Load(path, maxEntries: 1000);
        history.RecordSubmitted("   ");
        history.RecordSubmitted("");
        history.RecordSubmitted("\n");

        Assert.Empty(history.Entries);
    }

    [Fact]
    public void Navigation_UsesDraftAndBounds()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "history.txt");

        var history = InputHistory.Load(path, maxEntries: 1000);
        history.RecordSubmitted("a");
        history.RecordSubmitted("b");

        var current = "draft";

        Assert.True(history.TryNavigate(current, -1, out var up1));
        Assert.Equal("b", up1);

        Assert.True(history.TryNavigate(up1, -1, out var up2));
        Assert.Equal("a", up2);

        Assert.False(history.TryNavigate(up2, -1, out _));

        Assert.True(history.TryNavigate(up2, 1, out var down1));
        Assert.Equal("b", down1);

        Assert.True(history.TryNavigate(down1, 1, out var down2));
        Assert.Equal("draft", down2);

        Assert.False(history.TryNavigate(down2, 1, out _));
    }

    [Fact]
    public void RecordSubmitted_AppliesMaxEntriesCap()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "history.txt");

        var history = InputHistory.Load(path, maxEntries: 3);
        history.RecordSubmitted("a");
        history.RecordSubmitted("b");
        history.RecordSubmitted("c");
        history.RecordSubmitted("d");

        Assert.Equal(new[] { "b", "c", "d" }, history.Entries);

        var history2 = InputHistory.Load(path, maxEntries: 3);
        Assert.Equal(new[] { "b", "c", "d" }, history2.Entries);
    }
}
