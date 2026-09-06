using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.App.Startup;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class WelcomeSetupExplicitDownloadTests
{
    [Fact]
    public void Missing_model_requires_download_before_finish()
    {
        var manager = new FakeModelManager { Downloaded = false };
        var vm = CreateViewModel(manager);

        Assert.True(vm.CanDownloadModel);
        Assert.False(vm.CanFinish);
        Assert.Equal("Download model", vm.DownloadActionText);
    }

    [Fact]
    public async Task Download_selected_model_downloads_only_and_enables_finish()
    {
        var manager = new FakeModelManager { Downloaded = false };
        var vm = CreateViewModel(manager);

        var downloaded = await vm.DownloadSelectedModelAsync(CancellationToken.None);

        Assert.True(downloaded);
        Assert.Equal(["download"], manager.Operations);
        Assert.False(vm.CanDownloadModel);
        Assert.True(vm.CanFinish);
        Assert.Equal(WelcomeSetupStage.ModelDownloaded, vm.Stage);
        Assert.Contains("downloaded", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_download_is_retryable_without_attempting_load()
    {
        var manager = new FakeModelManager
        {
            Downloaded = false,
            DownloadError = new HttpRequestException("network detail")
        };
        var vm = CreateViewModel(manager);

        var downloaded = await vm.DownloadSelectedModelAsync(CancellationToken.None);

        Assert.False(downloaded);
        Assert.Equal(["download"], manager.Operations);
        Assert.Equal(WelcomeSetupStage.Error, vm.Stage);
        Assert.Equal("Retry download", vm.DownloadActionText);
        Assert.True(vm.CanDownloadModel);
        Assert.DoesNotContain("network detail", vm.ErrorMessage);
    }

    private static WelcomeSetupViewModel CreateViewModel(FakeModelManager manager) =>
        new(
            new StartupReadinessResult(false, AppConfig.Default, StartupReadinessReason.ModelMissing),
            new FakeConfigStore(),
            manager);

    private sealed class FakeConfigStore : IAppConfigStore
    {
        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(AppConfig.Default);

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeModelManager : IModelManager
    {
        private readonly ModelOption _model = ModelCatalog.Options[0];
        private InferenceStatus _status = new(false);

        public IReadOnlyList<ModelOption> Catalog => [_model];
        public bool Downloaded { get; set; }
        public Exception? DownloadError { get; init; }
        public List<string> Operations { get; } = [];

        public bool IsDownloaded(ModelOption model) => Downloaded;
        public InferenceStatus GetStatus() => _status;

        public ValueTask DownloadAsync(
            ModelOption model,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            Operations.Add("download");
            if (DownloadError is not null)
                throw DownloadError;
            Downloaded = true;
            progress?.Report(new ModelDownloadProgress(100, 100));
            return ValueTask.CompletedTask;
        }

        public ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken)
        {
            Operations.Add("load");
            _status = new InferenceStatus(true, model.Repo, model.File, "Cpu", "CPU");
            return ValueTask.CompletedTask;
        }

        public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
