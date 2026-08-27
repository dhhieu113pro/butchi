using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.App.Startup;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class WelcomeSetupViewModelTests
{
    [Fact]
    public async Task Finish_saves_settings_downloads_missing_model_and_loads_it()
    {
        var store = new FakeConfigStore();
        var manager = new FakeModelManager { Downloaded = false };
        var vm = CreateViewModel(store, manager);
        vm.TargetLanguage = "Japanese";

        var completion = await vm.FinishAsync(CancellationToken.None);

        Assert.NotNull(completion);
        Assert.Equal("Japanese", completion.Config.TargetLanguage);
        Assert.Equal(ModelCatalog.Options[0].Repo, store.SavedConfig?.ModelRepo);
        Assert.Equal(["download", "load"], manager.Operations);
        Assert.Equal(WelcomeSetupStage.Ready, vm.Stage);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(1d, vm.DownloadProgress?.Fraction);
    }

    [Fact]
    public async Task Finish_does_not_download_an_existing_model()
    {
        var manager = new FakeModelManager { Downloaded = true };
        var vm = CreateViewModel(new FakeConfigStore(), manager);

        var completion = await vm.FinishAsync(CancellationToken.None);

        Assert.NotNull(completion);
        Assert.Equal(["load"], manager.Operations);
    }

    [Fact]
    public async Task Invalid_settings_and_operation_errors_remain_visible_and_retryable()
    {
        var manager = new FakeModelManager
        {
            Downloaded = true,
            LoadError = new InvalidOperationException("private model path")
        };
        var vm = CreateViewModel(new FakeConfigStore(), manager);
        vm.TargetLanguage = "   ";

        Assert.Null(await vm.FinishAsync(CancellationToken.None));
        Assert.Equal(WelcomeSetupStage.Error, vm.Stage);
        Assert.Contains("target language", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        vm.TargetLanguage = "English";
        Assert.Null(await vm.FinishAsync(CancellationToken.None));
        Assert.Equal(WelcomeSetupStage.Error, vm.Stage);
        Assert.DoesNotContain("private model path", vm.ErrorMessage);
        Assert.True(vm.CanFinish);
    }

    [Fact]
    public async Task Concurrent_finish_is_ignored_while_the_first_operation_is_running()
    {
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new FakeModelManager
        {
            Downloaded = true,
            LoadStarted = loadStarted,
            ReleaseLoad = releaseLoad
        };
        var vm = CreateViewModel(new FakeConfigStore(), manager);

        var first = vm.FinishAsync(CancellationToken.None).AsTask();
        await loadStarted.Task;
        var second = await vm.FinishAsync(CancellationToken.None);
        releaseLoad.SetResult();

        Assert.Null(second);
        Assert.NotNull(await first);
        Assert.Equal(1, manager.LoadCalls);
    }

    private static WelcomeSetupViewModel CreateViewModel(
        FakeConfigStore store,
        FakeModelManager manager) =>
        new(
            new StartupReadinessResult(false, AppConfig.Default, StartupReadinessReason.ModelMissing),
            store,
            manager);

    private sealed class FakeConfigStore : IAppConfigStore
    {
        public AppConfig? SavedConfig { get; private set; }

        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(SavedConfig ?? AppConfig.Default);

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            SavedConfig = config;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeModelManager : IModelManager
    {
        private readonly ModelOption _model = ModelCatalog.Options[0];

        public IReadOnlyList<ModelOption> Catalog => [_model];
        public bool Downloaded { get; set; }
        public Exception? LoadError { get; init; }
        public TaskCompletionSource? LoadStarted { get; init; }
        public TaskCompletionSource? ReleaseLoad { get; init; }
        public List<string> Operations { get; } = [];
        public int LoadCalls { get; private set; }
        private InferenceStatus Status { get; set; } = new(false);

        public bool IsDownloaded(ModelOption model) => Downloaded;
        public InferenceStatus GetStatus() => Status;

        public ValueTask DownloadAsync(ModelOption model, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            Operations.Add("download");
            Downloaded = true;
            progress?.Report(new ModelDownloadProgress(100, 100));
            return ValueTask.CompletedTask;
        }

        public async ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken)
        {
            Operations.Add("load");
            LoadCalls++;
            LoadStarted?.TrySetResult();
            if (ReleaseLoad is not null)
                await ReleaseLoad.Task.WaitAsync(cancellationToken);
            if (LoadError is not null)
                throw LoadError;
            Status = new InferenceStatus(true, model.Repo, model.File, "Cpu", "CPU");
        }

        public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
