using Cycon.BlockCommands;
using Cycon.Core.Styling;
using Cycon.Core.Transcript;
using Cycon.Core.Transcript.Blocks;
using Cycon.Host.Hosting;

namespace Cycon.Host.Commands;

internal interface IBlockCommandSession
{
    int AllocateNewBlockId();
    void InsertBlockAfter(BlockId afterId, IBlock block);
    bool TryGetPrompt(BlockId id, out PromptBlock prompt);
    void AppendOwnedPromptInternal(string promptText);
    void AttachIndicator(BlockId commandEchoId, BlockId activityBlockId);
    void OpenInspect(InspectKind kind, string path, string title, IBlock viewBlock, string receiptLine, BlockId commandEchoId);
    void ClearTranscript();
    void RequestExit();
    string HomeDirectory { get; }
    string CurrentDirectory { get; }
    string ResolvePath(string path);
    bool TrySetCurrentDirectory(string directory, out string error);
    IFileSystem FileSystem { get; }
    PromptCaretSettings GetPromptCaretSettings();
    void SetPromptCaretSettings(in PromptCaretSettings settings);
    void ShowHelpControlsOverlay();
}

internal interface IOverlayCommandContext
{
    void ShowHelpControlsOverlay();
}

internal sealed class BlockCommandContext : IBlockCommandContext, IFileCommandContext, IOverlayCommandContext
{
    private readonly IBlockCommandSession _session;
    private readonly BlockId _commandEchoId;
    private readonly BlockId _shellPromptId;
    private BlockId _insertAfterId;
    private bool _startedBlockingActivity;

    public BlockCommandContext(IBlockCommandSession session, BlockId commandEchoId, BlockId shellPromptId)
    {
        _session = session;
        _commandEchoId = commandEchoId;
        _shellPromptId = shellPromptId;
        _insertAfterId = commandEchoId;
    }

    public bool StartedBlockingActivity => _startedBlockingActivity;

    public BlockId CommandEchoId => _commandEchoId;

    public void InsertTextAfterCommandEcho(string text, ConsoleTextStream stream)
    {
        var id = new BlockId(_session.AllocateNewBlockId());
        _session.InsertBlockAfter(_insertAfterId, new TextBlock(id, text, stream));
        _insertAfterId = id;
    }

    public BlockId AllocateBlockId() => new(_session.AllocateNewBlockId());

    public void InsertBlockAfterCommandEcho(IBlock block)
    {
        _session.InsertBlockAfter(_insertAfterId, block);
        _insertAfterId = block.Id;

        if (block is IRunnableBlock runnable &&
            runnable.State == BlockRunState.Running &&
            block is not PromptBlock)
        {
            _startedBlockingActivity = true;
        }
    }

    public void OpenInspect(InspectKind kind, string path, string title, IBlock viewBlock, string receiptLine)
    {
        _startedBlockingActivity = true;
        _session.OpenInspect(kind, path, title, viewBlock, receiptLine, _commandEchoId);
    }

    public void AttachIndicator(BlockId activityBlockId)
    {
        _session.AttachIndicator(_commandEchoId, activityBlockId);
    }

    public void AppendOwnedPrompt(string promptText)
    {
        if (_session.TryGetPrompt(_shellPromptId, out var prompt))
        {
            prompt.Input = string.Empty;
            prompt.SetCaret(0);
        }

        _session.AppendOwnedPromptInternal(promptText);
    }

    public void ClearTranscript()
    {
        _session.ClearTranscript();
    }

    public void RequestExit()
    {
        _session.RequestExit();
    }

    public string CurrentDirectory => _session.CurrentDirectory;

    public string HomeDirectory => _session.HomeDirectory;

    public string ResolvePath(string path) => _session.ResolvePath(path);

    public bool TrySetCurrentDirectory(string directory, out string error) => _session.TrySetCurrentDirectory(directory, out error);

    public IFileSystem FileSystem => _session.FileSystem;

    public PromptCaretSettings GetPromptCaretSettings() => _session.GetPromptCaretSettings();

    public void SetPromptCaretSettings(in PromptCaretSettings settings) => _session.SetPromptCaretSettings(settings);

    public void ShowHelpControlsOverlay() => _session.ShowHelpControlsOverlay();
}
