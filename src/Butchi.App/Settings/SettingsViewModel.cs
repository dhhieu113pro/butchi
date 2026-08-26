using Butchi.Core.Configuration;

namespace Butchi.App.Settings;

public interface IAppConfigStore
{
    ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken);
}

public sealed class SettingsViewModel
{
    private readonly IAppConfigStore _store;
    private AppConfig _persisted;
    private AppConfig _workingCopy;

    private SettingsViewModel(IAppConfigStore store, AppConfig persisted)
    {
        _store = store;
        _persisted = persisted;
        _workingCopy = persisted;
    }

    public AppConfig WorkingCopy
    {
        get => _workingCopy;
        set => _workingCopy = value;
    }

    public bool HasUnsavedChanges => _workingCopy != _persisted;

    public bool RequiresModelReload { get; private set; }

    public static async ValueTask<SettingsViewModel> CreateAsync(
        IAppConfigStore store,
        CancellationToken cancellationToken)
    {
        var persisted = await store.LoadAsync(cancellationToken);
        return new SettingsViewModel(store, persisted);
    }

    public async ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        var previous = _persisted;
        await _store.SaveAsync(_workingCopy, cancellationToken);
        _persisted = _workingCopy;
        RequiresModelReload = RequiresReload(previous, _persisted);
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken)
    {
        _persisted = await _store.LoadAsync(cancellationToken);
        _workingCopy = _persisted;
    }

    public void RestoreDefaults() => _workingCopy = AppConfig.Default;

    private static bool RequiresReload(AppConfig before, AppConfig after) =>
        before.ModelRepo != after.ModelRepo ||
        before.ModelFile != after.ModelFile ||
        before.BackendPreference != after.BackendPreference ||
        before.GpuLayers != after.GpuLayers;
}
