using System.ComponentModel;
using System.Runtime.CompilerServices;
using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Inference;

namespace Butchi.App.Startup;

public enum WelcomeSetupStage
{
    NeedsSettings,
    NeedsModel,
    Downloading,
    Loading,
    Error,
    Ready
}

public sealed record WelcomeSetupCompletion(AppConfig Config);

public interface IWelcomeSetupViewModelFactory
{
    WelcomeSetupViewModel Create(StartupReadinessResult readiness);
}

public sealed class WelcomeSetupViewModelFactory(
    IAppConfigStore configStore,
    IModelManager modelManager) : IWelcomeSetupViewModelFactory
{
    public WelcomeSetupViewModel Create(StartupReadinessResult readiness) =>
        new(readiness, configStore, modelManager);
}

public sealed class WelcomeSetupViewModel : INotifyPropertyChanged
{
    private readonly IAppConfigStore _configStore;
    private readonly IModelManager _modelManager;
    private readonly SemaphoreSlim _finishGate = new(1, 1);
    private AppConfig _config;
    private AppThemePreference _theme;
    private string _targetLanguage;
    private ResultAction _resultAction;
    private ModelOption? _selectedModel;
    private ModelDownloadProgress? _downloadProgress;
    private WelcomeSetupStage _stage;
    private string? _errorMessage;
    private bool _isBusy;

    public WelcomeSetupViewModel(
        StartupReadinessResult readiness,
        IAppConfigStore configStore,
        IModelManager modelManager)
    {
        _configStore = configStore;
        _modelManager = modelManager;
        _config = readiness.Config;
        _theme = _config.Theme;
        _targetLanguage = _config.TargetLanguage;
        _resultAction = _config.ResultAction;
        _selectedModel = modelManager.Catalog.FirstOrDefault(model =>
            model.Repo == _config.ModelRepo && model.File == _config.ModelFile)
            ?? modelManager.Catalog.FirstOrDefault();
        _stage = InitialStage(readiness.Reason);
        _errorMessage = InitialError(readiness);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ModelOption> Catalog => _modelManager.Catalog;

    public AppThemePreference Theme
    {
        get => _theme;
        set => SetField(ref _theme, value);
    }

    public string TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            if (SetField(ref _targetLanguage, value))
                OnPropertyChanged(nameof(CanFinish));
        }
    }

    public ResultAction ResultAction
    {
        get => _resultAction;
        set => SetField(ref _resultAction, value);
    }

    public ModelOption? SelectedModel => _selectedModel;
    public ModelDownloadProgress? DownloadProgress => _downloadProgress;
    public WelcomeSetupStage Stage => _stage;
    public string? ErrorMessage => _errorMessage;
    public bool IsBusy => _isBusy;
    public bool CanFinish => !IsBusy && SelectedModel is not null && !string.IsNullOrWhiteSpace(TargetLanguage);

    public string StatusText => Stage switch
    {
        WelcomeSetupStage.NeedsSettings => "Confirm your settings to continue.",
        WelcomeSetupStage.NeedsModel => "Choose a local model to continue.",
        WelcomeSetupStage.Downloading => "Downloading the local model…",
        WelcomeSetupStage.Loading => "Loading the local model…",
        WelcomeSetupStage.Error => "Setup needs your attention.",
        WelcomeSetupStage.Ready => "Butchi is ready.",
        _ => string.Empty
    };

    public void SelectModel(ModelOption model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Catalog.Contains(model))
            throw new ArgumentException("Model must come from the current catalog.", nameof(model));
        if (SetField(ref _selectedModel, model, nameof(SelectedModel)))
            OnPropertyChanged(nameof(CanFinish));
    }

    public async ValueTask<WelcomeSetupCompletion?> FinishAsync(CancellationToken cancellationToken)
    {
        if (!await _finishGate.WaitAsync(0, cancellationToken))
            return null;

        SetBusy(true);
        SetError(null);
        try
        {
            var model = SelectedModel ?? throw new InvalidOperationException("Choose a model to continue.");
            var config = _config with
            {
                Theme = Theme,
                TargetLanguage = AppConfig.NormalizeTargetLanguage(TargetLanguage),
                ResultAction = ResultAction,
                ModelRepo = model.Repo,
                ModelFile = model.File
            };

            await _configStore.SaveAsync(config, cancellationToken);
            if (!_modelManager.IsDownloaded(model))
            {
                SetStage(WelcomeSetupStage.Downloading);
                var progress = new Progress<ModelDownloadProgress>(value =>
                {
                    _downloadProgress = value;
                    OnPropertyChanged(nameof(DownloadProgress));
                });
                await _modelManager.DownloadAsync(model, progress, cancellationToken);
            }

            SetStage(WelcomeSetupStage.Loading);
            await _modelManager.LoadAsync(model, cancellationToken);
            var status = _modelManager.GetStatus();
            if (!status.IsLoaded || status.ModelRepo != model.Repo || status.ModelFile != model.File)
                throw new InvalidOperationException("Selected model did not become ready.");

            _config = config;
            SetStage(WelcomeSetupStage.Ready);
            return new WelcomeSetupCompletion(config);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException)
        {
            SetFailure("Enter a valid target language and try again.");
            return null;
        }
        catch (HttpRequestException)
        {
            SetFailure("The model could not be downloaded. Check your connection and retry.");
            return null;
        }
        catch (IOException)
        {
            SetFailure("Setup could not access local files. Check access and retry.");
            return null;
        }
        catch (Exception)
        {
            SetFailure("The local model could not be loaded. Retry or choose another model.");
            return null;
        }
        finally
        {
            SetBusy(false);
            _finishGate.Release();
        }
    }

    private void SetFailure(string message)
    {
        SetError(message);
        SetStage(WelcomeSetupStage.Error);
    }

    private void SetBusy(bool value)
    {
        if (_isBusy == value) return;
        _isBusy = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanFinish));
    }

    private void SetStage(WelcomeSetupStage value)
    {
        if (_stage == value) return;
        _stage = value;
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(StatusText));
    }

    private void SetError(string? value)
    {
        if (_errorMessage == value) return;
        _errorMessage = value;
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private static WelcomeSetupStage InitialStage(StartupReadinessReason reason) => reason switch
    {
        StartupReadinessReason.SettingsMissing or
        StartupReadinessReason.SettingsInvalid or
        StartupReadinessReason.SettingsUnavailable => WelcomeSetupStage.NeedsSettings,
        StartupReadinessReason.ModelMissing => WelcomeSetupStage.NeedsModel,
        StartupReadinessReason.Ready => WelcomeSetupStage.Ready,
        _ => WelcomeSetupStage.Error
    };

    private static string? InitialError(StartupReadinessResult readiness) => readiness.Reason switch
    {
        StartupReadinessReason.SettingsInvalid => "Existing settings could not be read. Confirm and save them again.",
        StartupReadinessReason.SettingsUnavailable => "Settings are unavailable. Check local file access and retry.",
        StartupReadinessReason.ModelLoadFailed => "The configured local model could not be loaded.",
        StartupReadinessReason.RuntimeFailed => "Butchi could not finish starting.",
        _ => null
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
