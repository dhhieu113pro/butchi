using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.App.Startup;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Butchi.Infrastructure;
using Xunit;

namespace Butchi.App.Tests;

public sealed class StartupReadinessTests
{
    [Theory]
    [InlineData(ConfigLoadState.Missing, StartupReadinessReason.SettingsMissing)]
    [InlineData(ConfigLoadState.Invalid, StartupReadinessReason.SettingsInvalid)]
    [InlineData(ConfigLoadState.Unavailable, StartupReadinessReason.SettingsUnavailable)]
    public async Task Non_ready_settings_require_setup_without_loading_a_model(
        ConfigLoadState state,
        StartupReadinessReason expectedReason)
    {
        var manager = new FakeModelManager { Downloaded = true };
        var service = new StartupReadinessService(
            new FakeStartupConfigStore(new ConfigLoadResult(AppConfig.Default, state)),
            manager);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(0, manager.LoadCalls);
    }

    [Fact]
    public async Task Missing_configured_model_requires_setup()
    {
        var manager = new FakeModelManager { Downloaded = false };
        var service = CreateReadyConfigService(manager);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(StartupReadinessReason.ModelMissing, result.Reason);
        Assert.Equal(0, manager.LoadCalls);
    }

    [Fact]
    public async Task Existing_configured_model_is_loaded_before_startup_is_ready()
    {
        var manager = new FakeModelManager { Downloaded = true };
        var service = CreateReadyConfigService(manager);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.Equal(StartupReadinessReason.Ready, result.Reason);
        Assert.True(manager.Status.IsLoaded);
        Assert.Equal(ModelCatalog.Options[0].Repo, manager.Status.ModelRepo);
        Assert.Equal(1, manager.LoadCalls);
    }

    [Fact]
    public async Task Model_load_failure_requires_setup_without_exposing_exception_message()
    {
        var manager = new FakeModelManager
        {
            Downloaded = true,
            LoadError = new InvalidOperationException("private model path")
        };
        var service = CreateReadyConfigService(manager);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(StartupReadinessReason.ModelLoadFailed, result.Reason);
        Assert.Equal(nameof(InvalidOperationException), result.ErrorCode);
        Assert.DoesNotContain("private model path", result.ErrorCode);
    }

    [Fact]
    public async Task Startup_is_not_ready_when_load_returns_without_matching_loaded_status()
    {
        var manager = new FakeModelManager { Downloaded = true, ReportLoaded = false };
        var service = CreateReadyConfigService(manager);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal(StartupReadinessReason.ModelLoadFailed, result.Reason);
    }

    private static StartupReadinessService CreateReadyConfigService(FakeModelManager manager) =>
        new(
            new FakeStartupConfigStore(new ConfigLoadResult(AppConfig.Default, ConfigLoadState.Ready)),
            manager);

    private sealed class FakeStartupConfigStore(ConfigLoadResult result) : IStartupConfigStore
    {
        public ValueTask<ConfigLoadResult> LoadWithStatusAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);

        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(result.Config);

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeModelManager : IModelManager
    {
        private readonly ModelOption _option = ModelCatalog.Options[0];

        public IReadOnlyList<ModelOption> Catalog => [_option];
        public bool Downloaded { get; init; }
        public Exception? LoadError { get; init; }
        public bool ReportLoaded { get; init; } = true;
        public int LoadCalls { get; private set; }
        public InferenceStatus Status { get; private set; } = new(false);

        public bool IsDownloaded(ModelOption model) => Downloaded;
        public InferenceStatus GetStatus() => Status;
        public ValueTask DownloadAsync(ModelOption model, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken)
        {
            LoadCalls++;
            if (LoadError is not null)
                throw LoadError;

            if (ReportLoaded)
                Status = new InferenceStatus(true, model.Repo, model.File, "Cpu", "CPU");
            return ValueTask.CompletedTask;
        }

        public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
