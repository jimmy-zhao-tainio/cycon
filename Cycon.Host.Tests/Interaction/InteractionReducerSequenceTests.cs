using Cycon.Core;
using Cycon.Core.Scrolling;
using Cycon.Core.Selection;
using Cycon.Core.Settings;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Interaction;
using Cycon.Host.Input;
using Cycon.Layout;
using Cycon.Layout.Metrics;

namespace Cycon.Host.Tests.Interaction;

public sealed class InteractionReducerSequenceTests
{
    [Fact]
    public void DragSelectThenType_ClearsSelectionAndTargetsLastPrompt()
    {
        var (document, prompt, layout) = CreateDocument(text: "HELLO");
        var reducer = new InteractionReducer();

        DragSelect(reducer, layout, document.Transcript, row: 0, colStart: 0, colEnd: 5);
        Assert.True(reducer.State.Selection is { } r && r.Anchor != r.Caret);

        var actions = reducer.Handle(new InputEvent.Text('a'), layout, document.Transcript);
        ApplyActions(actions, reducer, document, clipboardText: null);

        Assert.Null(reducer.State.Selection);
        Assert.Equal("a", prompt.Input);
        Assert.Equal(1, prompt.CaretIndex);
    }

    [Fact]
    public void SelectAcrossPromptPrefix_CopiedTextExcludesPrefix()
    {
        var (document, prompt, layout) = CreateDocument(text: string.Empty, promptInput: "cmd");
        var reducer = new InteractionReducer();

        var promptRow = FindFirstRowForBlock(layout, prompt.Id);
        DragSelect(reducer, layout, document.Transcript, row: promptRow, colStart: 0, colEnd: 5);

        var actions = reducer.Handle(new InputEvent.KeyDown(HostKey.C, HostKeyModifiers.Control), layout, document.Transcript);
        var clipboard = ApplyActions(actions, reducer, document, clipboardText: null);

        Assert.Equal("cmd", clipboard);
    }

    [Fact]
    public void EscDuringSelection_ClearsCaptureSelectionAndFocusesLastPrompt()
    {
        var (document, prompt, layout) = CreateDocument(text: "HELLO");
        var reducer = new InteractionReducer();

        var (x, y) = CellToPixel(layout, row: 0, col: 1);
        reducer.Handle(new InputEvent.MouseDown(x, y, MouseButton.Left, HostKeyModifiers.None), layout, document.Transcript);

        Assert.True(reducer.State.IsSelecting);
        Assert.NotNull(reducer.State.MouseCaptured);
        Assert.NotNull(reducer.State.Selection);

        reducer.Handle(new InputEvent.KeyDown(HostKey.Escape, HostKeyModifiers.None), layout, document.Transcript);

        Assert.False(reducer.State.IsSelecting);
        Assert.Null(reducer.State.MouseCaptured);
        Assert.Null(reducer.State.Selection);
        Assert.Equal(prompt.Id, reducer.State.Focused);
    }

    [Fact]
    public void CtrlV_PastesIntoPromptAndClearsSelection()
    {
        var (document, prompt, layout) = CreateDocument(text: "HELLO");
        var reducer = new InteractionReducer();

        DragSelect(reducer, layout, document.Transcript, row: 0, colStart: 0, colEnd: 5);
        Assert.NotNull(reducer.State.Selection);

        var actions = reducer.Handle(new InputEvent.KeyDown(HostKey.V, HostKeyModifiers.Control), layout, document.Transcript);
        ApplyActions(actions, reducer, document, clipboardText: "XYZ");

        Assert.Equal("XYZ", prompt.Input);
        Assert.Equal(3, prompt.CaretIndex);
        Assert.Null(reducer.State.Selection);
    }

    [Fact]
    public void MouseCaptureBlocksNewSelectionStart()
    {
        var (document, _, layout) = CreateDocument(text: "HELLO");
        var reducer = new InteractionReducer();

        var (x0, y0) = CellToPixel(layout, row: 0, col: 0);
        reducer.Handle(new InputEvent.MouseDown(x0, y0, MouseButton.Left, HostKeyModifiers.None), layout, document.Transcript);
        Assert.NotNull(reducer.State.MouseCaptured);

        var (x1, y1) = CellToPixel(layout, row: 0, col: 2);
        var actions = reducer.Handle(new InputEvent.MouseDown(x1, y1, MouseButton.Left, HostKeyModifiers.None), layout, document.Transcript);

        Assert.Empty(actions);
        Assert.NotNull(reducer.State.MouseCaptured);
        Assert.True(reducer.State.IsSelecting);
    }

    [Fact]
    public void EnterOnEmptyPrompt_InsertsPromptEchoLine()
    {
        var (document, prompt, layout) = CreateDocument(text: string.Empty, promptInput: string.Empty);
        var reducer = new InteractionReducer();

        var actions = reducer.Handle(new InputEvent.KeyDown(HostKey.Enter, HostKeyModifiers.None), layout, document.Transcript);
        ApplyActions(actions, reducer, document, clipboardText: null);

        Assert.Equal(string.Empty, prompt.Input);
        Assert.Equal(0, prompt.CaretIndex);
        Assert.Equal(3, document.Transcript.Blocks.Count);
        Assert.IsType<TextBlock>(document.Transcript.Blocks[1]);
        Assert.Equal("> ", ((TextBlock)document.Transcript.Blocks[1]).Text);
    }

    [Fact]
    public void EnterOnNonEmptyPrompt_InsertsEchoThenUnrecognizedOutputLine()
    {
        var (document, prompt, layout) = CreateDocument(text: string.Empty, promptInput: "foo");
        var reducer = new InteractionReducer();

        var actions = reducer.Handle(new InputEvent.KeyDown(HostKey.Enter, HostKeyModifiers.None), layout, document.Transcript);
        ApplyActions(actions, reducer, document, clipboardText: null);

        Assert.Equal(string.Empty, prompt.Input);
        Assert.Equal(0, prompt.CaretIndex);
        Assert.Equal(4, document.Transcript.Blocks.Count);
        Assert.Equal("> foo", ((TextBlock)document.Transcript.Blocks[1]).Text);
        Assert.Equal("Unrecognized command.", ((TextBlock)document.Transcript.Blocks[2]).Text);
    }

    private static void DragSelect(
        InteractionReducer reducer,
        LayoutFrame layout,
        Transcript transcript,
        int row,
        int colStart,
        int colEnd)
    {
        var (x0, y0) = CellToPixel(layout, row, colStart);
        var (x1, y1) = CellToPixel(layout, row, colEnd);
        reducer.Handle(new InputEvent.MouseDown(x0, y0, MouseButton.Left, HostKeyModifiers.None), layout, transcript);
        reducer.Handle(new InputEvent.MouseMove(x1, y1, HostKeyModifiers.None), layout, transcript);
        reducer.Handle(new InputEvent.MouseUp(x1, y1, MouseButton.Left, HostKeyModifiers.None), layout, transcript);
    }

    private static int FindFirstRowForBlock(LayoutFrame layout, BlockId blockId)
    {
        foreach (var line in layout.Lines)
        {
            if (line.BlockId == blockId)
            {
                return line.RowIndex;
            }
        }

        throw new InvalidOperationException($"Block {blockId} not found in layout.");
    }

    private static (int X, int Y) CellToPixel(LayoutFrame layout, int row, int col)
    {
        var grid = layout.Grid;
        return (
            grid.PaddingLeftPx + (col * grid.CellWidthPx) + 1,
            grid.PaddingTopPx + (row * grid.CellHeightPx) + 1);
    }

    private static (ConsoleDocument Document, PromptBlock Prompt, LayoutFrame Layout) CreateDocument(
        string text,
        string promptInput = "",
        int caretIndex = 0,
        int cols = 40,
        int rows = 10)
    {
        var transcript = new Transcript();
        transcript.Add(new TextBlock(new BlockId(1), text));
        var prompt = new PromptBlock(new BlockId(2), "> ")
        {
            Input = promptInput,
            CaretIndex = caretIndex
        };
        transcript.Add(prompt);

        var document = new ConsoleDocument(
            transcript,
            new InputState(),
            new ScrollState(),
            new SelectionState(),
            new ConsoleSettings());

        var settings = new LayoutSettings
        {
            CellWidthPx = 8,
            CellHeightPx = 16,
            PaddingPolicy = PaddingPolicy.None
        };

        var viewport = new ConsoleViewport(cols * settings.CellWidthPx, rows * settings.CellHeightPx);
        var layout = new LayoutEngine().Layout(document, settings, viewport);
        return (document, prompt, layout);
    }

    private static string? ApplyActions(
        IReadOnlyList<HostAction> actions,
        InteractionReducer reducer,
        ConsoleDocument document,
        string? clipboardText)
    {
        foreach (var action in actions)
        {
            switch (action)
            {
                case HostAction.InsertText insert:
                    if (TryGetPrompt(document.Transcript, insert.PromptId, out var insertPrompt))
                    {
                        insertPrompt.InsertText(insert.Text);
                    }
                    break;
                case HostAction.Backspace backspace:
                    if (TryGetPrompt(document.Transcript, backspace.PromptId, out var backspacePrompt))
                    {
                        backspacePrompt.Backspace();
                    }
                    break;
                case HostAction.MoveCaret moveCaret:
                    if (TryGetPrompt(document.Transcript, moveCaret.PromptId, out var moveCaretPrompt))
                    {
                        moveCaretPrompt.MoveCaret(moveCaret.Delta);
                    }
                    break;
                case HostAction.SetCaret setCaret:
                    if (TryGetPrompt(document.Transcript, setCaret.PromptId, out var setCaretPrompt))
                    {
                        setCaretPrompt.SetCaret(setCaret.Index);
                    }
                    break;
                case HostAction.SubmitPrompt submit:
                    CommitPrompt(document.Transcript, submit.PromptId);
                    break;
                case HostAction.CopySelectionToClipboard:
                    if (reducer.TryGetSelectedText(document.Transcript, out var selected))
                    {
                        clipboardText = selected;
                    }
                    break;
                case HostAction.PasteFromClipboardIntoLastPrompt:
                    if (!string.IsNullOrEmpty(clipboardText) &&
                        reducer.State.LastPromptId is { } lastPromptId &&
                        TryGetPrompt(document.Transcript, lastPromptId, out var pastePrompt))
                    {
                        pastePrompt.InsertText(clipboardText);
                    }
                    break;
            }
        }

        return clipboardText;
    }

    private static void CommitPrompt(Transcript transcript, BlockId promptId)
    {
        if (!TryGetPrompt(transcript, promptId, out var prompt))
        {
            return;
        }

        var command = prompt.Input;
        var insertIndex = Math.Max(0, transcript.Blocks.Count - 1);
        transcript.Insert(insertIndex, new TextBlock(new BlockId(AllocateNewBlockId(transcript)), prompt.Prompt + command));
        if (!string.IsNullOrWhiteSpace(command))
        {
            transcript.Insert(insertIndex + 1, new TextBlock(new BlockId(AllocateNewBlockId(transcript)), "Unrecognized command."));
        }

        prompt.Input = string.Empty;
        prompt.SetCaret(0);
    }

    private static int AllocateNewBlockId(Transcript transcript)
    {
        var max = 0;
        foreach (var block in transcript.Blocks)
        {
            max = Math.Max(max, block.Id.Value);
        }

        return max + 1;
    }

    private static bool TryGetPrompt(Transcript transcript, BlockId id, out PromptBlock prompt)
    {
        foreach (var block in transcript.Blocks)
        {
            if (block.Id == id && block is PromptBlock p)
            {
                prompt = p;
                return true;
            }
        }

        prompt = null!;
        return false;
    }
}
