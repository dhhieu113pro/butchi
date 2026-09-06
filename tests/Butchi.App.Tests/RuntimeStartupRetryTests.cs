using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.App.Startup;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class RuntimeStartupRetryTests
{
    [Fact]
    public async Task Runtime_failure_is_presented_as_runtime_retry_without_reloading_ready_model()
    {
        var model = ModelCatalog.Options[0];
        var config = AppConfig.Default with
        {
            ModelRepo = model.Repo,
            ModelFile = model.File
        };
        var manager = new ReadyModelManager(model);
        var viewModel = new WelcomeSetupViewModel(
            new StartupReadinessResult(
                false,
                config,
                StartupReadinessReason.RuntimeFailed,
                "Win32Exception (5): Access is denied."),
            new FakeConfigStore(config),
            manager);

        Assert.Equal("Retry startup", viewModel.PrimaryActionText);
        Assert.Contains("runtime", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Win32Exception (5)", viewModel.ErrorMessage, StringComparison.Ordinal);

        var completion = await viewModel.FinishAsync(CancellationToken.None);

        Assert.NotNull(completion);
        Assert.Equal(0, manager.LoadCalls);
    }

    [Fact]
    public async Task Coordinator_forwards_runtime_exception_details_to_retry_setup()
    {
        var model = ModelCatalog.Options[0];
        var config = AppConfig.Default with
        {
            ModelRepo = model.Repo,
            ModelFile = model.File
        };
        var captured = new List<StartupReadinessResult>();
        var manager = new ReadyModelManager(model);
        var coordinator = new StartupCoordinator(
            new ReadyReadiness(config),
            new CapturingWelcomeFactory(captured, manager),
            new ClosingWelcomeHost(),
            new ThrowingRuntimeFactory(),
            () => { });

        await coordinator.RunAsync(CancellationToken.None);

        var failure = Assert.Single(captured);
        Assert.Equal(StartupReadinessReason.RuntimeFailed, failure.Reason);
        Assert.Contains("InvalidOperationException", failure.ErrorCode, StringComparison.Ordinal);
        Assert.Contains("runtime start exploded", failure.ErrorCode, StringComparison.Ordinal);
    }

    private sealed class ReadyReadiness(AppConfig config) : IStartupReadinessService
    {
        public ValueTask<StartupReadinessResult> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new StartupReadinessResult(true, config, StartupReadinessReason.Ready));
    }

    private sealed class CapturingWelcomeFactory(
        List<StartupReadinessResult> captured,
        IModelManager manager) : IWelcomeSetupViewModelFactory
    {
        public WelcomeSetupViewModel Create(StartupReadinessResult readiness)
        {
            captured.Add(readiness);
            return new WelcomeSetupViewModel(readiness, new FakeConfigStore(readiness.Config), manager);
        }
    }

    private sealed class ClosingWelcomeHost : IWelcomeSetupHost
    {
        public ValueTask<WelcomeSetupCompletion?> ShowAsync(
            WelcomeSetupViewModel viewModel,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<WelcomeSetupCompletion?>(null);
    }

    private sealed class ThrowingRuntimeFactory : IButchiRuntimeFactory
    {
        public ValueTask<IButchiRuntime> CreateAsync(AppConfig config, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("runtime start exploded");
    }

    private sealed class FakeConfigStore(AppConfig config) : IAppConfigStore
    {
        private AppConfig _config = config;

        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(_config);

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            _config = config;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReadyModelManager(ModelOption model) : IModelManager
    {
        private InferenceStatus _status = new(true, model.Repo, model.File, "Cpu", "CPU");

        public IReadOnlyList<ModelOption> Catalog { get; } = [model];
        public int LoadCalls { get; private set; }
        public bool IsDownloaded(ModelOption candidate) => Equals(candidate, model);
        public InferenceStatus GetStatus() => _status;
        public ValueTask DownloadAsync(
            ModelOption candidate,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask LoadAsync(ModelOption candidate, CancellationToken cancellationToken)
        {
            LoadCalls++;
            _status = new InferenceStatus(true, candidate.Repo, candidate.File, "Cpu", "CPU");
            return ValueTask.CompletedTask;
        }

        public ValueTask UnloadAsync(CancellationToken cancellationToken)
        {
            _status = new InferenceStatus(false);
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(ModelOption candidate, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
