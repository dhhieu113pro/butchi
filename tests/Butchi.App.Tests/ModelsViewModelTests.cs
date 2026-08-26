using Butchi.App.Models;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class ModelsViewModelTests
{
    [Fact]
    public async Task Refresh_maps_catalog_downloaded_and_loaded_state()
    {
        var option = ModelCatalog.Options[0];
        var manager = new FakeModelManager(option)
        {
            Downloaded = true,
            Status = new InferenceStatus(true, option.Repo, option.File, "Vulkan", "RTX A1000")
        };
        var vm = new ModelsViewModel(manager);

        await vm.RefreshAsync(CancellationToken.None);

        var item = Assert.Single(vm.Items);
        Assert.True(item.IsDownloaded);
        Assert.True(item.IsLoaded);
        Assert.Equal("Vulkan", vm.ActualBackend);
        Assert.Equal("RTX A1000", vm.ActualDevice);
    }

    [Fact]
    public async Task Download_and_load_refresh_state()
    {
        var option = ModelCatalog.Options[0];
        var manager = new FakeModelManager(option);
        var vm = new ModelsViewModel(manager);

        await vm.DownloadAsync(option, CancellationToken.None);
        Assert.True(manager.Downloaded);

        await vm.LoadAsync(option, CancellationToken.None);
        Assert.True(manager.Status.IsLoaded);
        Assert.True(Assert.Single(vm.Items).IsLoaded);
    }

    [Fact]
    public async Task Delete_unloads_loaded_model_before_removing_file()
    {
        var option = ModelCatalog.Options[0];
        var manager = new FakeModelManager(option)
        {
            Downloaded = true,
            Status = new InferenceStatus(true, option.Repo, option.File)
        };
        var vm = new ModelsViewModel(manager);

        await vm.DeleteAsync(option, CancellationToken.None);

        Assert.Equal(new[] { "unload", "delete" }, manager.Operations);
        Assert.False(manager.Downloaded);
    }

    [Fact]
    public void First_run_routes_to_models_when_no_usable_model_exists()
    {
        Assert.True(FirstRunPolicy.ShouldOpenModels(new InferenceStatus(false), configuredModelExists: false));
        Assert.False(FirstRunPolicy.ShouldOpenModels(new InferenceStatus(true), configuredModelExists: true));
    }

    private sealed class FakeModelManager(ModelOption option) : IModelManager
    {
        public IReadOnlyList<ModelOption> Catalog => [option];
        public bool Downloaded { get; set; }
        public InferenceStatus Status { get; set; } = new(false);
        public List<string> Operations { get; } = [];

        public bool IsDownloaded(ModelOption model) => Downloaded;
        public InferenceStatus GetStatus() => Status;

        public ValueTask DownloadAsync(ModelOption model, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            Downloaded = true;
            progress?.Report(new ModelDownloadProgress(1, 1));
            return ValueTask.CompletedTask;
        }

        public ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken)
        {
            Status = new InferenceStatus(true, model.Repo, model.File, "Cpu", "CPU");
            return ValueTask.CompletedTask;
        }

        public ValueTask UnloadAsync(CancellationToken cancellationToken)
        {
            Operations.Add("unload");
            Status = new InferenceStatus(false);
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken)
        {
            Operations.Add("delete");
            Downloaded = false;
            return ValueTask.CompletedTask;
        }
    }
}
