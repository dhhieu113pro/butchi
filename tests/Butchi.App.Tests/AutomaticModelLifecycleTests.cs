using System.ComponentModel;
using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class AutomaticModelLifecycleTests
{
    [Fact]
    public async Task Selecting_missing_model_downloads_then_loads_automatically()
    {
        var first = ModelCatalog.Options[0];
        var second = ModelCatalog.Options[1];
        var manager = new FakeModelManager([first, second]);
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await ModelManagementViewModel.CreateAsync(manager, store, CancellationToken.None);

        vm.SelectModel(second);

        await WaitUntilAsync(() => manager.Operations.Count >= 2);
        Assert.Equal(new[] { $"download:{second.Id}", $"load:{second.Id}" }, manager.Operations);
    }

    [Fact]
    public async Task Selecting_downloaded_model_skips_download_and_loads_automatically()
    {
        var first = ModelCatalog.Options[0];
        var second = ModelCatalog.Options[1];
        var manager = new FakeModelManager([first, second]);
        manager.Downloaded.Add(second.Id);
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await ModelManagementViewModel.CreateAsync(manager, store, CancellationToken.None);

        vm.SelectModel(second);

        await WaitUntilAsync(() => manager.Operations.Count >= 1);
        Assert.Equal(new[] { $"load:{second.Id}" }, manager.Operations);
    }

    [Fact]
    public async Task Model_view_model_notifies_background_progress_changes()
    {
        var option = ModelCatalog.Options[0];
        var manager = new FakeModelManager([option]);
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await ModelManagementViewModel.CreateAsync(manager, store, CancellationToken.None);

        Assert.IsAssignableFrom<INotifyPropertyChanged>(vm);
    }

    [Fact]
    public void Model_view_hides_manual_download_and_load_buttons()
    {
        var root = FindRepositoryRoot();
        var viewPath = Path.Combine(root, "src", "Butchi.App", "Models", "ModelManagementView.cs");
        var source = File.ReadAllText(viewPath);

        Assert.DoesNotContain("Download model", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Load model", source, StringComparison.Ordinal);
        Assert.Contains("DownloadProgress", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_management_disables_automatic_model_preparation()
    {
        var root = FindRepositoryRoot();
        var factoryPath = Path.Combine(root, "src", "Butchi.App", "Startup", "ButchiRuntimeFactory.cs");
        var viewPath = Path.Combine(root, "src", "Butchi.App", "Models", "ModelManagementView.cs");
        var factorySource = File.ReadAllText(factoryPath);
        var viewSource = File.ReadAllText(viewPath);

        Assert.Contains("autoPrepareModel: false", factorySource, StringComparison.Ordinal);
        Assert.Contains("bool autoPrepareModel = true", viewSource, StringComparison.Ordinal);
        Assert.Contains("if (autoPrepareModel)", viewSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_error_state_exposes_retry_for_current_selection()
    {
        var root = FindRepositoryRoot();
        var viewPath = Path.Combine(root, "src", "Butchi.App", "Models", "ModelManagementView.cs");
        var source = File.ReadAllText(viewPath);

        Assert.Contains("Retry", source, StringComparison.Ordinal);
        Assert.Contains("EnsureSelectedModelReady", source, StringComparison.Ordinal);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                break;
            await Task.Delay(20);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Butchi.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Butchi repository root.");
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

    private sealed class FakeModelManager(IReadOnlyList<ModelOption> catalog) : IModelManager
    {
        public IReadOnlyList<ModelOption> Catalog { get; } = catalog;
        public HashSet<string> Downloaded { get; } = [];
        public List<string> Operations { get; } = [];
        private InferenceStatus _status = new(false);

        public bool IsDownloaded(ModelOption model) => Downloaded.Contains(model.Id);
        public InferenceStatus GetStatus() => _status;

        public ValueTask DownloadAsync(ModelOption model, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add($"download:{model.Id}");
            progress?.Report(new ModelDownloadProgress(50, 100));
            Downloaded.Add(model.Id);
            progress?.Report(new ModelDownloadProgress(100, 100));
            return ValueTask.CompletedTask;
        }

        public ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add($"load:{model.Id}");
            _status = new InferenceStatus(true, model.Repo, model.File, "Auto", "Local device");
            return ValueTask.CompletedTask;
        }

        public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
