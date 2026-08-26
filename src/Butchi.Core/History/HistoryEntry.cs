namespace Butchi.Core.History;

public sealed record HistoryEntry(
    string Id,
    long TimestampMs,
    string Action,
    string Source,
    string Result,
    string Message,
    string? TargetLanguage = null);
