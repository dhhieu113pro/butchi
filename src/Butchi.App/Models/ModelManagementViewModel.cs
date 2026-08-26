using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Inference;

namespace Butchi.App.Models;

public sealed class ModelManagementViewModel
{
    private readonly IModelManager _manager;
    private readonly IAppConfigStore _configStore;
    private AppConfig _config;

    private ModelManagementViewModel(IModelManager manager, IAppConfigStore configStore, AppConfig config)
    {
        _manager = manager;
        _configStore = configStore;
        _config = config;
        SelectedModel = SelectConfiguredModel(config) ?? manager.Catalog.FirstOrDefault();
    }

    public IReadOnlyList<ModelOption> Catalog => _manager.Catalog;
    public ModelOption? SelectedModel { get; private set; }
    public bool IsSetupRequired { get; private set; }
    public bool IsLoaded { get; private set; }
    public string? ActualBackend { get; private set; }
    public string? ActualDevice { get; private set; }
    public ModelDownloadProgress? DownloadProgress { get; private set; }
    public BackendPreference BackendPreference => _config.BackendPreference;
    public uint MaxTokens => _config.MaxTokens;
    public float Temperature => _config.Temperature;
    public uint GpuLayers => _config.GpuLayers;
    public string SaveStatus { get; private set; } = "Saved";

    public static async ValueTask<ModelManagementViewModel> CreateAsync(
        IModelManager manager,
        IAppConfigStore configStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(configStore);

        var config = await configStore.LoadAsync(cancellationToken);
        var viewModel = new ModelManagementViewModel(manager, configStore, config);
        await viewModel.RefreshAsync(cancellationToken);
        return viewModel;
    }

    public ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = _manager.GetStatus();
        IsLoaded = status.IsLoaded;
        ActualBackend = status.ActualBackend;
        ActualDevice = status.ActualDevice;
        IsSetupRequired = !status.IsLoaded && !_manager.Catalog.Any(_manager.IsDownloaded);

        if (SelectedModel is null)
            SelectedModel = SelectConfiguredModel(_config) ?? _manager.Catalog.FirstOrDefault();

        return ValueTask.CompletedTask;
    }

    public void SelectModel(ModelOption model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!_manager.Catalog.Contains(model))
            throw new ArgumentException("Model must come from the current catalog.", nameof(model));
        SelectedModel = model;
    }

    public async ValueTask DownloadAsync(CancellationToken cancellationToken)
    {
        var model = RequireSelectedModel();
        var progress = new Progress<ModelDownloadProgress>(value => DownloadProgress = value);
        await _manager.DownloadAsync(model, progress, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async ValueTask LoadAsync(CancellationToken cancellationToken)
    {
        var model = RequireSelectedModel();
        await _manager.LoadAsync(model, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async ValueTask SetBackendPreferenceAsync(BackendPreference value, CancellationToken cancellationToken) =>
        await SaveAsync(_config with { BackendPreference = value }, cancellationToken);

    public async ValueTask SetMaxTokensAsync(uint value, CancellationToken cancellationToken) =>
        await SaveAsync(_config with { MaxTokens = Math.Clamp(value, 32u, 4096u) }, cancellationToken);

    public async ValueTask SetTemperatureAsync(float value, CancellationToken cancellationToken) =>
        await SaveAsync(_config with { Temperature = Math.Clamp(value, 0f, 2f) }, cancellationToken);

    public async ValueTask SetGpuLayersAsync(uint value, CancellationToken cancellationToken) =>
        await SaveAsync(_config with { GpuLayers = Math.Clamp(value, 0u, 999u) }, cancellationToken);

    private ModelOption? SelectConfiguredModel(AppConfig config) =>
        _manager.Catalog.FirstOrDefault(model => model.Repo == config.ModelRepo && model.File == config.ModelFile);

    private ModelOption RequireSelectedModel() =>
        SelectedModel ?? throw new InvalidOperationException("No model is available in the catalog.");

    private async ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
    {
        SaveStatus = "Saving";
        try
        {
            await _configStore.SaveAsync(config, cancellationToken);
            _config = config;
            SaveStatus = "Saved";
        }
        catch
        {
            SaveStatus = "Error";
            throw;
        }
    }
}
