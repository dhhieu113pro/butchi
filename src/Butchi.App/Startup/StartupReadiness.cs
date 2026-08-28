using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Infrastructure;

namespace Butchi.App.Startup;

public enum StartupReadinessReason
{
    Ready,
    SettingsMissing,
    SettingsInvalid,
    SettingsUnavailable,
    ModelMissing,
    ModelLoadFailed,
    RuntimeFailed
}

public sealed record StartupReadinessResult(
    bool IsReady,
    AppConfig Config,
    StartupReadinessReason Reason,
    string? ErrorCode = null);

public interface IStartupReadinessService
{
    ValueTask<StartupReadinessResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed class StartupReadinessService(
    IStartupConfigStore configStore,
    IModelManager modelManager) : IStartupReadinessService
{
    public async ValueTask<StartupReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        var configResult = await configStore.LoadWithStatusAsync(cancellationToken);
        var settingsReason = configResult.State switch
        {
            ConfigLoadState.Ready => StartupReadinessReason.Ready,
            ConfigLoadState.Missing => StartupReadinessReason.SettingsMissing,
            ConfigLoadState.Invalid => StartupReadinessReason.SettingsInvalid,
            ConfigLoadState.Unavailable => StartupReadinessReason.SettingsUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(configResult.State), configResult.State, null)
        };

        if (settingsReason != StartupReadinessReason.Ready)
        {
            return new StartupReadinessResult(
                false,
                configResult.Config,
                settingsReason,
                configResult.ErrorCode);
        }

        var configuredModel = modelManager.Catalog.FirstOrDefault(model =>
            model.Repo == configResult.Config.ModelRepo &&
            model.File == configResult.Config.ModelFile);
        if (configuredModel is null || !modelManager.IsDownloaded(configuredModel))
        {
            return new StartupReadinessResult(
                false,
                configResult.Config,
                StartupReadinessReason.ModelMissing);
        }

        try
        {
            await modelManager.LoadAsync(configuredModel, configResult.Config, cancellationToken);
            var status = modelManager.GetStatus();
            var isReady = status.IsLoaded &&
                          status.ModelRepo == configuredModel.Repo &&
                          status.ModelFile == configuredModel.File;
            return isReady
                ? new StartupReadinessResult(true, configResult.Config, StartupReadinessReason.Ready)
                : new StartupReadinessResult(false, configResult.Config, StartupReadinessReason.ModelLoadFailed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StartupReadinessResult(
                false,
                configResult.Config,
                StartupReadinessReason.ModelLoadFailed,
                ex.GetType().Name);
        }
    }
}
