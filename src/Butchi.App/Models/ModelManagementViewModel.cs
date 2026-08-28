using System.ComponentModel;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Inference;

namespace Butchi.App.Models;

public enum ModelLifecycleState
{
    Idle,
    Checking,
    Downloading,
    Loading,
    Ready,
    Error
}

public sealed class ModelManagementViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IModelManager _manager;
    private readonly IAppConfigStore _configStore;
    private readonly object _modelOperationGate = new();
    private AppConfig _config;
    private CancellationTokenSource? _modelOperationCts;
    private bool _disposed;

    private ModelManagementViewModel(IModelManager manager, IAppConfigStore configStore, AppConfig config)
    {
        _manager = manager;
        _configStore = configStore;
        _config = config;
        SelectedModel = SelectConfiguredModel(config) ?? manager.Catalog.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ModelOption> Catalog => _manager.Catalog;
    public ModelOption? SelectedModel { get; private set; }
    public bool IsSetupRequired { get; private set; }
    public bool IsLoaded { get; private set; }
    public string? ActualBackend { get; private set; }
    public string? ActualDevice { get; private set; }
    public ModelDownloadProgress? DownloadProgress { get; private set; }
    public ModelLifecycleState LifecycleState { get; private set; } = ModelLifecycleState.Idle;
    public string? LifecycleError { get; private set; }
    public bool IsModelOperationActive => LifecycleState is ModelLifecycleState.Checking or ModelLifecycleState.Downloading or ModelLifecycleState.Loading;
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
        SetValue(ref IsLoaded, status.IsLoaded, nameof(IsLoaded));
        SetValue(ref ActualBackend, status.ActualBackend, nameof(ActualBackend));
        SetValue(ref ActualDevice, status.ActualDevice, nameof(ActualDevice));
        SetValue(ref IsSetupRequired, !status.IsLoaded && !_manager.Catalog.Any(_manager.IsDownloaded), nameof(IsSetupRequired));

        if (SelectedModel is null)
        {
            SelectedModel = SelectConfiguredModel(_config) ?? _manager.Catalog.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedModel));
        }

        return ValueTask.CompletedTask;
    }

    public void EnsureSelectedModelReady()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SelectedModel is { } model)
            StartModelLifecycle(model, persistSelection: false);
    }

    public void SelectModel(ModelOption model)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(model);
        if (!_manager.Catalog.Contains(model))
            throw new ArgumentException("Model must come from the current catalog.", nameof(model));

        if (Equals(SelectedModel, model) && IsLoadedFor(model))
            return;

        SelectedModel = model;
        OnPropertyChanged(nameof(SelectedModel));
        StartModelLifecycle(model, persistSelection: true);
    }

    public async ValueTask DownloadAsync(CancellationToken cancellationToken)
    {
        var model = RequireSelectedModel();
        SetDownloadProgress(null);
        var progress = new CallbackProgress<ModelDownloadProgress>(SetDownloadProgress);
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancellationTokenSource? operation;
        lock (_modelOperationGate)
        {
            operation = _modelOperationCts;
            _modelOperationCts = null;
        }

        operation?.Cancel();
    }

    private void StartModelLifecycle(ModelOption model, bool persistSelection)
    {
        var operation = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_modelOperationGate)
        {
            previous = _modelOperationCts;
            _modelOperationCts = operation;
        }

        previous?.Cancel();
        _ = Task.Run(() => RunModelLifecycleAsync(model, persistSelection, operation));
    }

    private async Task RunModelLifecycleAsync(ModelOption model, bool persistSelection, CancellationTokenSource operation)
    {
        var cancellationToken = operation.Token;
        try
        {
            SetLifecycle(operation, ModelLifecycleState.Checking, null);
            SetDownloadProgress(operation, null);

            if (persistSelection)
            {
                await SaveAsync(_config with
                {
                    ModelRepo = model.Repo,
                    ModelFile = model.File
                }, cancellationToken).ConfigureAwait(false);
            }

            if (IsLoadedFor(model))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
                SetLifecycle(operation, ModelLifecycleState.Ready, null);
                return;
            }

            if (!_manager.IsDownloaded(model))
            {
                SetLifecycle(operation, ModelLifecycleState.Downloading, null);
                var progress = new CallbackProgress<ModelDownloadProgress>(value => SetDownloadProgress(operation, value));
                await _manager.DownloadAsync(model, progress, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            SetLifecycle(operation, ModelLifecycleState.Loading, null);
            await _manager.LoadAsync(model, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            SetDownloadProgress(operation, null);
            SetLifecycle(operation, ModelLifecycleState.Ready, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetLifecycle(operation, ModelLifecycleState.Idle, null);
        }
        catch (Exception ex)
        {
            SetLifecycle(operation, ModelLifecycleState.Error, ex.Message);
        }
        finally
        {
            lock (_modelOperationGate)
            {
                if (ReferenceEquals(_modelOperationCts, operation))
                    _modelOperationCts = null;
            }

            operation.Dispose();
        }
    }

    private bool IsLoadedFor(ModelOption model)
    {
        var status = _manager.GetStatus();
        return status.IsLoaded
            && string.Equals(status.ModelRepo, model.Repo, StringComparison.Ordinal)
            && string.Equals(status.ModelFile, model.File, StringComparison.Ordinal);
    }

    private void SetLifecycle(CancellationTokenSource operation, ModelLifecycleState state, string? error)
    {
        if (!IsCurrentOperation(operation))
            return;

        LifecycleState = state;
        LifecycleError = error;
        OnPropertyChanged(nameof(LifecycleState));
        OnPropertyChanged(nameof(LifecycleError));
        OnPropertyChanged(nameof(IsModelOperationActive));
    }

    private void SetDownloadProgress(ModelDownloadProgress? value)
    {
        DownloadProgress = value;
        OnPropertyChanged(nameof(DownloadProgress));
    }

    private void SetDownloadProgress(CancellationTokenSource operation, ModelDownloadProgress? value)
    {
        if (IsCurrentOperation(operation))
            SetDownloadProgress(value);
    }

    private bool IsCurrentOperation(CancellationTokenSource operation)
    {
        lock (_modelOperationGate)
            return ReferenceEquals(_modelOperationCts, operation);
    }

    private ModelOption? SelectConfiguredModel(AppConfig config) =>
        _manager.Catalog.FirstOrDefault(model => model.Repo == config.ModelRepo && model.File == config.ModelFile);

    private ModelOption RequireSelectedModel() =>
        SelectedModel ?? throw new InvalidOperationException("No model is available in the catalog.");

    private async ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
    {
        SaveStatus = "Saving";
        OnPropertyChanged(nameof(SaveStatus));
        try
        {
            await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
            _config = config;
            SaveStatus = "Saved";
            OnPropertyChanged(nameof(SaveStatus));
        }
        catch
        {
            SaveStatus = "Error";
            OnPropertyChanged(nameof(SaveStatus));
            throw;
        }
    }

    private void SetValue<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
