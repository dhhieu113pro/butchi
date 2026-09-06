using System.ComponentModel;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Inference;
using Butchi.Infrastructure;

namespace Butchi.App.Models;

public sealed record VisionModelDownloadProgress(
    string FileName,
    int FileIndex,
    int FileCount,
    ModelDownloadProgress Progress)
{
    public double? Fraction => Progress.Fraction;
}

public interface IVisionModelManager
{
    IReadOnlyList<VisionModelOption> Catalog { get; }
    bool IsDownloaded(VisionModelOption model);
    ValueTask DownloadAsync(
        VisionModelOption model,
        IProgress<VisionModelDownloadProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class FileVisionModelManager(AppPaths paths, ModelDownloader downloader) : IVisionModelManager
{
    public IReadOnlyList<VisionModelOption> Catalog => VisionModelCatalog.Options;

    public bool IsDownloaded(VisionModelOption model) =>
        File.Exists(paths.ModelPath(model.Repo, model.ModelFile)) &&
        File.Exists(paths.ModelPath(model.Repo, model.ProjectorFile));

    public async ValueTask DownloadAsync(
        VisionModelOption model,
        IProgress<VisionModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        var files = new[] { model.ModelFile, model.ProjectorFile };
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var path = paths.ModelPath(model.Repo, file);
            if (File.Exists(path))
                continue;

            IProgress<ModelDownloadProgress>? fileProgress = progress is null
                ? null
                : new CallbackProgress<ModelDownloadProgress>(value =>
                    progress.Report(new VisionModelDownloadProgress(file, index + 1, files.Length, value)));

            await downloader.DownloadAsync(model.Repo, file, path, fileProgress, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}

public sealed class VisionModelManagementViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IVisionModelManager _manager;
    private readonly IAppConfigStore _configStore;
    private readonly object _operationGate = new();
    private AppConfig _config;
    private CancellationTokenSource? _operationCts;
    private bool _disposed;
    private bool _isDownloaded;

    private VisionModelManagementViewModel(
        IVisionModelManager manager,
        IAppConfigStore configStore,
        AppConfig config)
    {
        _manager = manager;
        _configStore = configStore;
        _config = config;
        SelectedModel = SelectConfiguredModel(config) ?? manager.Catalog.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<VisionModelOption> Catalog => _manager.Catalog;
    public VisionModelOption? SelectedModel { get; private set; }
    public bool IsDownloaded => _isDownloaded;
    public VisionModelDownloadProgress? DownloadProgress { get; private set; }
    public ModelLifecycleState LifecycleState { get; private set; } = ModelLifecycleState.Idle;
    public string? LifecycleError { get; private set; }
    public string SaveStatus { get; private set; } = "Saved";

    public static async ValueTask<VisionModelManagementViewModel> CreateAsync(
        IVisionModelManager manager,
        IAppConfigStore configStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(configStore);

        var config = await configStore.LoadAsync(cancellationToken);
        var viewModel = new VisionModelManagementViewModel(manager, configStore, config);
        viewModel.Refresh(cancellationToken);
        return viewModel;
    }

    public void EnsureSelectedModelReady()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (SelectedModel is { } model)
            StartLifecycle(model, persistSelection: false);
    }

    public void SelectModel(VisionModelOption model)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(model);
        if (!_manager.Catalog.Contains(model))
            throw new ArgumentException("Vision model must come from the current catalog.", nameof(model));

        SelectedModel = model;
        OnPropertyChanged(nameof(SelectedModel));
        SetDownloaded(_manager.IsDownloaded(model));
        StartLifecycle(model, persistSelection: true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancellationTokenSource? operation;
        lock (_operationGate)
        {
            operation = _operationCts;
            _operationCts = null;
        }

        operation?.Cancel();
    }

    private void Refresh(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetDownloaded(SelectedModel is { } model && _manager.IsDownloaded(model));
    }

    private void StartLifecycle(VisionModelOption model, bool persistSelection)
    {
        var operation = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_operationGate)
        {
            previous = _operationCts;
            _operationCts = operation;
        }

        previous?.Cancel();
        _ = Task.Run(() => RunLifecycleAsync(model, persistSelection, operation));
    }

    private async Task RunLifecycleAsync(
        VisionModelOption model,
        bool persistSelection,
        CancellationTokenSource operation)
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
                    VisionModelRepo = model.Repo,
                    VisionModelFile = model.ModelFile,
                    VisionProjectorFile = model.ProjectorFile
                }, cancellationToken).ConfigureAwait(false);
            }

            if (_manager.IsDownloaded(model))
            {
                SetDownloaded(operation, true);
                SetLifecycle(operation, ModelLifecycleState.Ready, null);
                return;
            }

            SetLifecycle(operation, ModelLifecycleState.Downloading, null);
            var progress = new CallbackProgress<VisionModelDownloadProgress>(value =>
                SetDownloadProgress(operation, value));
            await _manager.DownloadAsync(model, progress, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            SetDownloaded(operation, _manager.IsDownloaded(model));
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
            lock (_operationGate)
            {
                if (ReferenceEquals(_operationCts, operation))
                    _operationCts = null;
            }

            operation.Dispose();
        }
    }

    private VisionModelOption? SelectConfiguredModel(AppConfig config) =>
        _manager.Catalog.FirstOrDefault(model =>
            model.Repo == config.VisionModelRepo &&
            model.ModelFile == config.VisionModelFile &&
            model.ProjectorFile == config.VisionProjectorFile);

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

    private void SetDownloaded(bool value)
    {
        if (_isDownloaded == value)
            return;
        _isDownloaded = value;
        OnPropertyChanged(nameof(IsDownloaded));
    }

    private void SetDownloaded(CancellationTokenSource operation, bool value)
    {
        if (IsCurrentOperation(operation))
            SetDownloaded(value);
    }

    private void SetLifecycle(CancellationTokenSource operation, ModelLifecycleState state, string? error)
    {
        if (!IsCurrentOperation(operation))
            return;

        LifecycleState = state;
        LifecycleError = error;
        OnPropertyChanged(nameof(LifecycleState));
        OnPropertyChanged(nameof(LifecycleError));
    }

    private void SetDownloadProgress(CancellationTokenSource operation, VisionModelDownloadProgress? value)
    {
        if (!IsCurrentOperation(operation))
            return;

        DownloadProgress = value;
        OnPropertyChanged(nameof(DownloadProgress));
    }

    private bool IsCurrentOperation(CancellationTokenSource operation)
    {
        lock (_operationGate)
            return ReferenceEquals(_operationCts, operation);
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
