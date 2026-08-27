using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.App.Startup;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task Ready_startup_starts_tray_without_showing_setup()
    {
        var runtime = new FakeRuntime();
        var setup = new FakeWelcomeHost();
        var coordinator = CreateCoordinator(new ImmediateReadiness(Ready()), setup, new FakeRuntimeFactory(runtime));

        await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(StartupCoordinatorState.Running, coordinator.State);
        Assert.Equal(1, runtime.StartCalls);
        Assert.Equal(0, setup.ShowCalls);
    }

    [Fact]
    public async Task Setup_completion_transitions_to_tray_without_restart()
    {
        var runtime = new FakeRuntime();
        var setup = new FakeWelcomeHost(new WelcomeSetupCompletion(AppConfig.Default));
        var coordinator = CreateCoordinator(new ImmediateReadiness(NotReady()), setup, new FakeRuntimeFactory(runtime));

        await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(1, setup.ShowCalls);
        Assert.Equal(1, runtime.StartCalls);
        Assert.Equal(StartupCoordinatorState.Running, coordinator.State);
    }

    [Fact]
    public async Task Closing_incomplete_setup_exits_without_creating_runtime()
    {
        var shutdownCalls = 0;
        var factory = new FakeRuntimeFactory(new FakeRuntime());
        var coordinator = CreateCoordinator(
            new ImmediateReadiness(NotReady()),
            new FakeWelcomeHost(null),
            factory,
            () => shutdownCalls++);

        await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(1, shutdownCalls);
        Assert.Equal(0, factory.CreateCalls);
        Assert.Equal(StartupCoordinatorState.Exiting, coordinator.State);
    }

    [Fact]
    public async Task Run_returns_control_while_genuinely_async_readiness_is_pending()
    {
        var pendingReadiness = new TaskCompletionSource<StartupReadinessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime();
        var coordinator = CreateCoordinator(
            new PendingReadiness(pendingReadiness.Task),
            new FakeWelcomeHost(),
            new FakeRuntimeFactory(runtime));

        var startup = coordinator.RunAsync(CancellationToken.None);

        Assert.False(startup.IsCompleted);
        Assert.Equal(StartupCoordinatorState.Checking, coordinator.State);
        pendingReadiness.SetResult(Ready());
        await startup;
        Assert.Equal(1, runtime.StartCalls);
    }

    [Fact]
    public async Task Runtime_failure_returns_to_setup_and_retries_without_restart()
    {
        var runtime = new FakeRuntime();
        var setup = new FakeWelcomeHost(new WelcomeSetupCompletion(AppConfig.Default));
        var factory = new FakeRuntimeFactory(runtime) { FailuresRemaining = 1 };
        var coordinator = CreateCoordinator(new ImmediateReadiness(Ready()), setup, factory);

        await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(1, setup.ShowCalls);
        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(1, runtime.StartCalls);
        Assert.Equal(StartupCoordinatorState.Running, coordinator.State);
    }

    private static StartupCoordinator CreateCoordinator(
        IStartupReadinessService readiness,
        IWelcomeSetupHost setup,
        IButchiRuntimeFactory runtimeFactory,
        Action? shutdown = null) =>
        new(readiness, new FakeWelcomeViewModelFactory(), setup, runtimeFactory, shutdown ?? (() => { }));

    private static StartupReadinessResult Ready() =>
        new(true, AppConfig.Default, StartupReadinessReason.Ready);

    private static StartupReadinessResult NotReady() =>
        new(false, AppConfig.Default, StartupReadinessReason.ModelMissing);

    private sealed class ImmediateReadiness(StartupReadinessResult result) : IStartupReadinessService
    {
        public ValueTask<StartupReadinessResult> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);
    }

    private sealed class PendingReadiness(Task<StartupReadinessResult> task) : IStartupReadinessService
    {
        public ValueTask<StartupReadinessResult> CheckAsync(CancellationToken cancellationToken) => new(task);
    }

    private sealed class FakeWelcomeHost(WelcomeSetupCompletion? completion = null) : IWelcomeSetupHost
    {
        public int ShowCalls { get; private set; }

        public ValueTask<WelcomeSetupCompletion?> ShowAsync(WelcomeSetupViewModel viewModel, CancellationToken cancellationToken)
        {
            ShowCalls++;
            return ValueTask.FromResult(completion);
        }
    }

    private sealed class FakeWelcomeViewModelFactory : IWelcomeSetupViewModelFactory
    {
        public WelcomeSetupViewModel Create(StartupReadinessResult readiness) =>
            new(readiness, new NoOpConfigStore(), new EmptyModelManager());
    }

    private sealed class FakeRuntimeFactory(FakeRuntime runtime) : IButchiRuntimeFactory
    {
        public int CreateCalls { get; private set; }
        public int FailuresRemaining { get; init; }

        public ValueTask<IButchiRuntime> CreateAsync(AppConfig config, CancellationToken cancellationToken)
        {
            CreateCalls++;
            if (CreateCalls <= FailuresRemaining)
                throw new InvalidOperationException("Simulated runtime failure.");
            return ValueTask.FromResult<IButchiRuntime>(runtime);
        }
    }

    private sealed class FakeRuntime : IButchiRuntime
    {
        public int StartCalls { get; private set; }
        public bool IsTrayStarted { get; private set; }
        public void StartTray() { StartCalls++; IsTrayStarted = true; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpConfigStore : IAppConfigStore
    {
        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(AppConfig.Default);
        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class EmptyModelManager : IModelManager
    {
        public IReadOnlyList<ModelOption> Catalog => [];
        public bool IsDownloaded(ModelOption model) => false;
        public InferenceStatus GetStatus() => new(false);
        public ValueTask DownloadAsync(ModelOption model, IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
