using System.ComponentModel;
using System.Runtime.CompilerServices;
using Butchi.Core.History;
using Butchi.Infrastructure;

namespace Butchi.App.History;

public interface IHistoryStore
{
    ValueTask<IReadOnlyList<HistoryEntry>> SearchAsync(string? query, string? action, int? limit, CancellationToken cancellationToken);
    ValueTask DeleteAsync(string id, CancellationToken cancellationToken);
    ValueTask ClearAsync(CancellationToken cancellationToken);
}

public interface IHistoryClipboard
{
    ValueTask SetTextAsync(string text, CancellationToken cancellationToken);
}

public sealed class SqliteHistoryStoreAdapter(SqliteHistoryStore inner) : IHistoryStore
{
    public async ValueTask<IReadOnlyList<HistoryEntry>> SearchAsync(string? query, string? action, int? limit, CancellationToken cancellationToken) =>
        await inner.SearchAsync(query, action, limit, cancellationToken);

    public async ValueTask DeleteAsync(string id, CancellationToken cancellationToken) =>
        await inner.DeleteAsync(id, cancellationToken);

    public async ValueTask ClearAsync(CancellationToken cancellationToken) =>
        await inner.ClearAsync(cancellationToken);
}

public sealed class HistoryViewModel(IHistoryStore store, IHistoryClipboard clipboard) : INotifyPropertyChanged
{
    private IReadOnlyList<HistoryEntry> _items = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? Query { get; set; }
    public string? ActionFilter { get; set; }
    public int? Limit { get; set; } = 200;

    public IReadOnlyList<HistoryEntry> Items
    {
        get => _items;
        private set
        {
            if (ReferenceEquals(_items, value))
                return;
            _items = value;
            OnPropertyChanged();
        }
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        Items = await store.SearchAsync(Query, ActionFilter, Limit, cancellationToken);
    }

    public async ValueTask DeleteAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await store.DeleteAsync(entry.Id, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async ValueTask ClearAsync(bool confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed)
            return;

        await store.ClearAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public ValueTask CopySourceAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return clipboard.SetTextAsync(entry.Source, cancellationToken);
    }

    public ValueTask CopyResultAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return clipboard.SetTextAsync(entry.Result, cancellationToken);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
