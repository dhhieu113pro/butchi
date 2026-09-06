using Avalonia.Controls;
using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Butchi.Infrastructure;

namespace Butchi.App.Startup;

internal static class FirstRunE2EStartup
{
    private const string Flag = "--e2e-first-run";

    public static bool TryCreate(
        string[] args,
        Action shutdown,
        out StartupCoordinator? coordinator)
    {
        var flagIndex = Array.FindIndex(
            args,
            value => string.Equals(value, Flag, StringComparison.OrdinalIgnoreCase));
        if (flagIndex < 0)
        {
            coordinator = null;
            return false;
        }

        var dataDirectory = flagIndex + 1 < args.Length &&
                            !args[flagIndex + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[flagIndex + 1]
            : Path.Combine(Path.GetTempPath(), "butchi-e2e-first-run", Guid.NewGuid().ToString("N"));

        var paths = new AppPaths(dataDirectory);
        paths.EnsureDirectories();
        var configStore = new JsonAppConfigStoreAdapter(new JsonConfigStore(paths));
        var modelManager = new DeterministicModelManager();

        coordinator = new StartupCoordinator(
            new StartupReadinessService(configStore, modelManager),
            new WelcomeSetupViewModelFactory(configStore, modelManager),
            new WelcomeSetupHost(),
            new DeterministicRuntimeFactory(),
            shutdown);
        return true;
    }

    private sealed class DeterministicModelManager : IModelManager
    {
        private static readonly ModelOption Model = new(
            "e2e-first-run",
            "Butchi E2E model",
            "butchi/e2e",
            "butchi-e2e.gguf",
            "1 KB");

        private bool _downloaded;
        private InferenceStatus _status = new(false);

        public IReadOnlyList<ModelOption> Catalog { get; } = [Model];

        public bool IsDownloaded(ModelOption model) => _downloaded && Equals(model, Model);

        public InferenceStatus GetStatus() => _status;

        public async ValueTask DownloadAsync(
            ModelOption model,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            RequireModel(model);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ModelDownloadProgress(0, 1024));
            await Task.Delay(100, cancellationToken);
            progress?.Report(new ModelDownloadProgress(512, 1024));
            await Task.Delay(100, cancellationToken);
            _downloaded = true;
            progress?.Report(new ModelDownloadProgress(1024, 1024));
        }

        public ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken) =>
            LoadAsync(model, AppConfig.Default with
            {
                ModelRepo = model.Repo,
                ModelFile = model.File
            }, cancellationToken);

        public ValueTask LoadAsync(
            ModelOption model,
            AppConfig config,
            CancellationToken cancellationToken)
        {
            RequireModel(model);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_downloaded)
                throw new InvalidOperationException("The deterministic E2E model must be downloaded before loading.");

            _status = new InferenceStatus(
                true,
                model.Repo,
                model.File,
                "e2e",
                "deterministic");
            return ValueTask.CompletedTask;
        }

        public ValueTask UnloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _status = new InferenceStatus(false);
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken)
        {
            RequireModel(model);
            cancellationToken.ThrowIfCancellationRequested();
            _downloaded = false;
            _status = new InferenceStatus(false);
            return ValueTask.CompletedTask;
        }

        private static void RequireModel(ModelOption model)
        {
            if (!Equals(model, Model))
                throw new ArgumentException("Unknown deterministic E2E model.", nameof(model));
        }
    }

    private sealed class DeterministicRuntimeFactory : IButchiRuntimeFactory
    {
        public ValueTask<IButchiRuntime> CreateAsync(
            AppConfig config,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IButchiRuntime>(new DeterministicRuntime());
        }
    }

    private sealed class DeterministicRuntime : IButchiRuntime
    {
        private readonly Window _settings = new()
        {
            Title = "Butchi Settings",
            Width = 720,
            Height = 480,
            Content = new TextBlock { Text = "First-run E2E completed." }
        };

        public bool IsTrayStarted { get; private set; }

        public void StartTray()
        {
            if (IsTrayStarted)
                return;

            _settings.Show();
            IsTrayStarted = true;
        }

        public ValueTask DisposeAsync()
        {
            if (_settings.IsVisible)
                _settings.Close();
            IsTrayStarted = false;
            return ValueTask.CompletedTask;
        }
    }
}
