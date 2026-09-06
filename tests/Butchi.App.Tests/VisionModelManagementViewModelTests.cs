using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class VisionModelManagementViewModelTests
{
    [Fact]
    public async Task Create_selects_configured_vision_model_and_reports_download_state()
    {
        var first = new VisionModelOption("example/vision-a", "vision-a.gguf", "mmproj-a.gguf", "Vision A");
        var second = new VisionModelOption("example/vision-b", "vision-b.gguf", "mmproj-b.gguf", "Vision B");
        var manager = new FakeVisionModelManager(first, second) { Downloaded = true };
        var store = new FakeConfigStore(AppConfig.Default with
        {
            VisionModelRepo = second.Repo,
            VisionModelFile = second.ModelFile,
            VisionProjectorFile = second.ProjectorFile
        });

        var vm = await VisionModelManagementViewModel.CreateAsync(manager, store, CancellationToken.None);

        Assert.Equal(second, vm.SelectedModel);
        Assert.True(vm.IsDownloaded);
        Assert.Equal("Vision B", second.ToString());
    }

    [Fact]
    public async Task Selecting_missing_vision_model_persists_and_downloads_pair_automatically()
    {
        var first = new VisionModelOption("example/vision-a", "vision-a.gguf", "mmproj-a.gguf", "Vision A");
        var second = new VisionModelOption("example/vision-b", "vision-b.gguf", "mmproj-b.gguf", "Vision B");
        var manager = new FakeVisionModelManager(first, second);
        var store = new FakeConfigStore(AppConfig.Default);
        using var vm = await VisionModelManagementViewModel.CreateAsync(manager, store, CancellationToken.None);

        vm.SelectModel(second);
        await manager.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await manager.AllowDownload.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => vm.LifecycleState == ModelLifecycleState.Ready);

        Assert.Equal(second.Repo, store.Value.VisionModelRepo);
        Assert.Equal(second.ModelFile, store.Value.VisionModelFile);
        Assert.Equal(second.ProjectorFile, store.Value.VisionProjectorFile);
        Assert.Equal(new[] { second }, manager.Downloads);
        Assert.True(vm.IsDownloaded);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate())
        {
            if (DateTime.UtcNow >= timeout)
                throw new TimeoutException("Timed out waiting for vision model lifecycle.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeConfigStore(AppConfig initial) : IAppConfigStore
    {
        public AppConfig Value { get; private set; } = initial;

        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Value);

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Value = config;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeVisionModelManager(params VisionModelOption[] options) : IVisionModelManager
    {
        public IReadOnlyList<VisionModelOption> Catalog { get; } = options;
        public bool Downloaded { get; set; }
        public List<VisionModelOption> Downloads { get; } = [];
        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowDownload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsDownloaded(VisionModelOption model) => Downloaded;

        public async ValueTask DownloadAsync(
            VisionModelOption model,
            IProgress<VisionModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            Downloads.Add(model);
            DownloadStarted.TrySetResult();
            AllowDownload.TrySetResult();
            await AllowDownload.Task.WaitAsync(cancellationToken);
            Downloaded = true;
            progress?.Report(new VisionModelDownloadProgress(model.ModelFile, 1, 2, new ModelDownloadProgress(1, 1)));
            progress?.Report(new VisionModelDownloadProgress(model.ProjectorFile, 2, 2, new ModelDownloadProgress(1, 1)));
        }
    }
}
