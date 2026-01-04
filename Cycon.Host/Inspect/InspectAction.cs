using Cycon.Core.Transcript;

namespace Cycon.Host.Inspect;

internal abstract record InspectAction;

internal sealed record InspectRequestContentRebuild : InspectAction;

internal sealed record InspectHandleFileDrop(string Path) : InspectAction;

internal sealed record InspectWriteReceipt(BlockId CommandEchoId, string ReceiptLine) : InspectAction;
