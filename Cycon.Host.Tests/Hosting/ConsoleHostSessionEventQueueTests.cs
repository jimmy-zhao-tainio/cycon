using Cycon.Backends.Abstractions;
using Cycon.Backends.Abstractions.Rendering;
using Cycon.Host.Hosting;
using Cycon.Host.Input;

namespace Cycon.Host.Tests.Hosting;

public sealed class ConsoleHostSessionEventQueueTests
{
    [Fact]
    public void TextInputThenMouseDownWithoutInterveningTick_UsesFreshLayoutForHitTest()
    {
        var clipboard = new FakeClipboard();
        var session = ConsoleHostSession.CreateVga(string.Empty, clipboard, resizeSettleMs: 0, rebuildThrottleMs: 0);

        session.Initialize(initialFbW: 50, initialFbH: 86);
        session.Tick();

        for (var i = 0; i < 10; i++)
        {
            session.OnTextInput(new HostTextInputEvent('a'));
        }

        // Click-drag on the second wrapped line of the prompt input (row=1).
        // Padding: left=5, top=3. Cell: 8x16.
        session.OnMouseEvent(new HostMouseEvent(HostMouseEventKind.Down, X: 6, Y: 20, HostMouseButtons.Left, HostKeyModifiers.None, 0));
        session.OnMouseEvent(new HostMouseEvent(HostMouseEventKind.Move, X: 30, Y: 20, HostMouseButtons.Left, HostKeyModifiers.None, 0));
        session.OnMouseEvent(new HostMouseEvent(HostMouseEventKind.Up, X: 30, Y: 20, HostMouseButtons.Left, HostKeyModifiers.None, 0));

        var tick = session.Tick();

        Assert.Contains(tick.Frame.Commands, c => c is DrawQuad q && q.Rgba == unchecked((int)0xEEEEEEFF));
    }

    private sealed class FakeClipboard : IClipboard
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;

        public string? GetText() => Text;
    }
}

