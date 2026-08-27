using System.ComponentModel;
using System.Runtime.CompilerServices;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Core.History;
using Butchi.Infrastructure;

namespace Butchi.App.History;

public interface IHistoryStore
{
    ValueTask<IReadOnlyList<HistoryEntry>> SearchAsync(string? query, string? action, int? limit, CancellationToken cancellationToken);
    ValueTask DeleteAsync(string id, CancellationToken cancellationToken);
    ValueTask ClearAsync(CancellationToken cancellationToken);
    ValueTask ApplyRetentionAsync(int retentionDays, long nowMs, CancellationToken cancellationToken);
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

    public async ValueTask ApplyRetentionAsync(int retentionDays, long nowMs, CancellationToken cancellationToken) =>
        await inner.ApplyRetentionAsync(retentionDays, nowMs, cancellationToken);
}

public sealed class HistoryViewModel : INotifyPropertyChanged
{
    private readonly IHistoryStore _store;
    private readonly IHistoryClipboard _clipboard;
    private readonly IAppConfigStore _configStore;
    private AppConfig _config;
    private IReadOnlyList<HistoryEntry> _items = [];
    private bool _isLoading;
    private string? _errorMessage;

    private HistoryViewModel(IHistoryStore store, IHistoryClipboard clipboard, IAppConfigStore configStore, AppConfig config)
    {
        _store = store;
        _clipboard = clipboard;
        _configStore = configStore;
        _config = config;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? Query { get; set; }
    public string? ActionFilter { get; set; }
    public int? Limit { get; set; } = 200;
    public int RetentionDays => _config.HistoryRetentionDays;
    public string SaveStatus { get; private set; } = "Saved";
    public bool IsEmpty => !IsLoading && ErrorMessage is null && Items.Count == 0;

    public bool IsLoading
    {
        get => _isLoading;
        private set { if (_isLoading == value) return; _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { if (_errorMessage == value) return; _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); }
    }

    public IReadOnlyList<HistoryEntry> Items
    {
        get => _items;
        private set
        {
            _items = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public static async ValueTask<HistoryViewModel> CreateAsync(
        IHistoryStore store,
        IHistoryClipboard clipboard,
        IAppConfigStore configStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(configStore);
        var config = await configStore.LoadAsync(cancellationToken);
        var viewModel = new HistoryViewModel(store, clipboard, configStore, config);
        await viewModel.RefreshAsync(cancellationToken);
        return viewModel;
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Items = await _store.SearchAsync(Normalize(Query), Normalize(ActionFilter), Limit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Items = [];
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async ValueTask DeleteAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _store.DeleteAsync(entry.Id, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async ValueTask ClearAsync(bool confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed) return;
        await _store.ClearAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public ValueTask CopySourceAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _clipboard.SetTextAsync(entry.Source, cancellationToken);
    }

    public ValueTask CopyResultAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _clipboard.SetTextAsync(entry.Result, cancellationToken);
    }

    public async ValueTask SetRetentionDaysAsync(int days, long nowMs, CancellationToken cancellationToken)
    {
        var normalized = Math.Clamp(days, 0, 3650);
        SaveStatus = "Saving";
        OnPropertyChanged(nameof(SaveStatus));
        try
        {
            var updated = _config with { HistoryRetentionDays = normalized };
            await _configStore.SaveAsync(updated, cancellationToken);
            await _store.ApplyRetentionAsync(normalized, nowMs, cancellationToken);
            _config = updated;
            SaveStatus = "Saved";
            OnPropertyChanged(nameof(RetentionDays));
            await RefreshAsync(cancellationToken);
        }
        catch
        {
            SaveStatus = "Error";
            throw;
        }
        finally
        {
            OnPropertyChanged(nameof(SaveStatus));
        }
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
