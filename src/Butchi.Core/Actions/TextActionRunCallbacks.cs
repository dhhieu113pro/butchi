namespace Butchi.Core.Actions;

public sealed record TextActionRunCallbacks(
    Action<long>? Started = null,
    Action<long, string>? Chunk = null,
    Action<long, string>? ReasoningChunk = null);
