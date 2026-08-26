using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class ModelManagementViewModelTests
{
    [Fact]
    public async Task Create_maps_setup_state_runtime_status_and_advanced_settings()
    {
        var option = ModelCatalog.Options[0];
        var manager = new FakeModelManager(option)
        {
            Downloaded = false,
            Status = new InferenceStatus(false)
        };
        var store = new FakeConfigStore(AppConfig.Default with
        {
            BackendPreference = BackendPreference.Gpu,
            MaxTokens = 384,
            Temperature = 0.45f,
            GpuLayers = 22
        });

        var vm = await ModelManagementViewModel.CreateAsync(manager, store, CancellationToken.None);

        Assert.True(vm.IsSetupRequired);
        Assert.Equal(option, vm.SelectedModel);
        Assert.Equal(BackendPreference.Gpu, vm.BackendPreference);
        Assert.Equal(384u, vm.MaxTokens);
        Assert.Equal(0.45f, vm.Temperature);
        Assert.Equal(22u, vm.GpuLayers);
        Assert.False(vm.IsLoaded);
    }

    [Fact]
    public async Task Advanced_settings_autosave_without_changing_model_runtime()
    {
        var option = ModelCatalog.Options[0];
        var manager = new FakeModelManager(option);
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await ModelManagementViewModel.CreateAsync(manager, store, CancellationToken.None);

        await vm.SetBackendPreferenceAsync(BackendPreference.Cpu, CancellationToken.None);
        await vm.SetMaxTokensAsync(512, CancellationToken.None);
        await vm.SetTemperatureAsync(0.6f, CancellationToken.None);
        await vm.SetGpuLayersAsync(16, CancellationToken.None);

        Assert.Equal(4, store.SaveCalls);
        Assert.Equal(BackendPreference.Cpu, store.Value.BackendPreference);
        Assert.Equal(512u, store.Value.MaxTokens);
        Assert.Equal(0.6f, store.Value.Temperature);
        Assert.Equal(16u, store.Value.GpuLayers);
        Assert.Empty(manager.Operations);
        Assert.Equal("Saved", vm.SaveStatus);
    }

    [Fact]
    public async Task Download_and_load_delegate_to_existing_model_manager_and_refresh_ready_state()
    {
        var option = ModelCatalog.Options[0];
        var manager = new FakeModelManager(option);
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await ModelManagementViewModel.CreateAsync(manager, store, CancellationToken.None);

        await vm.DownloadAsync(CancellationToken.None);
        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(new[] { "download", "load" }, manager.Operations);
        Assert.False(vm.IsSetupRequired);
        Assert.True(vm.IsLoaded);
        Assert.Equal("Vulkan", vm.ActualBackend);
        Assert.Equal("GPU", vm.ActualDevice);
    }

    private sealed class FakeConfigStore(AppConfig initial) : IAppConfigStore
    {
        public AppConfig Value { get; private set; } = initial;
        public int SaveCalls { get; private set; }

        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Value);

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            Value = config;
            return ValueTask.CompletedTask;
        }
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
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("download");
            Downloaded = true;
            progress?.Report(new ModelDownloadProgress(1, 1));
            return ValueTask.CompletedTask;
        }

        public ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("load");
            Status = new InferenceStatus(true, model.Repo, model.File, "Vulkan", "GPU");
            return ValueTask.CompletedTask;
        }

        public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
