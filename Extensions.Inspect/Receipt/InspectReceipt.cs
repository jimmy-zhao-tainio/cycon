using System.Collections.Generic;

namespace Extensions.Inspect.Receipt;

public sealed record InspectReceipt(
    string TypeChip,
    string FileName,
    IReadOnlyList<string> MetaFields);

