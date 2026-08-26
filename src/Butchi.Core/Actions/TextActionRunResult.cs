namespace Butchi.Core.Actions;

public sealed record TextActionRunResult(
    TextAction Action,
    long RunId,
    string Output,
    bool IsObsolete);
