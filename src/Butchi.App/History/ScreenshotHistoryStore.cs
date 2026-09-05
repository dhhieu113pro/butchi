using Butchi.Core.History;

namespace Butchi.App.History;

internal sealed class ScreenshotHistoryStore(bool populated) : IHistoryStore
{
    private List<HistoryEntry> _entries = populated
        ?
        [
            new("shot-translate", 1_787_782_400_000L, "translate", "Good morning, how are you?", "Chào buổi sáng, bạn khỏe không?", "Completed locally", "Vietnamese"),
            new("shot-rewrite", 1_787_778_800_000L, "rewrite", "can you send report before lunch", "Could you send the report before lunch?", "Completed locally")
        ]
        : [];

    public ValueTask AppendAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.Insert(0, entry);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<HistoryEntry>> SearchAsync(string? query, string? action, int? limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<HistoryEntry> results = _entries;
        if (!string.IsNullOrWhiteSpace(query))
            results = results.Where(x => x.Source.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Result.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(action))
            results = results.Where(x => x.Action.Equals(action, StringComparison.OrdinalIgnoreCase));
        if (limit is { } count)
            results = results.Take(count);
        return ValueTask.FromResult<IReadOnlyList<HistoryEntry>>([.. results]);
    }

    public ValueTask DeleteAsync(string id, CancellationToken cancellationToken)
    {
        _entries.RemoveAll(x => x.Id == id);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        _entries.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplyRetentionAsync(int retentionDays, long nowMs, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
