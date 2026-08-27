using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.App.Startup;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;

namespace Butchi.App.Diagnostics;

public sealed record StartupBehaviorProbeResult(bool FirstRunCompositionReady, bool TrayReady);

public static class StartupBehaviorProbe
{
    public static async ValueTask<StartupBehaviorProbeResult> RunAsync(CancellationToken cancellationToken)
    {
        var runtime = new ProbeRuntime();
        var welcomeHost = new CompletingWelcomeHost();
        await using var coordinator = new StartupCoordinator(
            new FirstRunReadiness(),
            new ProbeWelcomeViewModelFactory(),
            welcomeHost,
            new ProbeRuntimeFactory(runtime),
            () => { });

        await coordinator.RunAsync(cancellationToken);
        return new StartupBehaviorProbeResult(
            welcomeHost.WasShown && coordinator.State == StartupCoordinatorState.Running,
            runtime.IsTrayStarted);
    }

    private sealed class FirstRunReadiness : IStartupReadinessService
    {
        public ValueTask<StartupReadinessResult> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new StartupReadinessResult(
                false,
                AppConfig.Default,
                StartupReadinessReason.SettingsMissing));
    }

    private sealed class ProbeWelcomeViewModelFactory : IWelcomeSetupViewModelFactory
    {
        public WelcomeSetupViewModel Create(StartupReadinessResult readiness) =>
            new(readiness, new ProbeConfigStore(), new ProbeModelManager());
    }

    private sealed class CompletingWelcomeHost : IWelcomeSetupHost
    {
        public bool WasShown { get; private set; }

        public ValueTask<WelcomeSetupCompletion?> ShowAsync(
            WelcomeSetupViewModel viewModel,
            CancellationToken cancellationToken)
        {
            WasShown = true;
            return ValueTask.FromResult<WelcomeSetupCompletion?>(new(AppConfig.Default));
        }
    }

    private sealed class ProbeRuntimeFactory(ProbeRuntime runtime) : IButchiRuntimeFactory
    {
        public ValueTask<IButchiRuntime> CreateAsync(AppConfig config, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IButchiRuntime>(runtime);
    }

    private sealed class ProbeRuntime : IButchiRuntime
    {
        public bool IsTrayStarted { get; private set; }
        public void StartTray() => IsTrayStarted = true;
        public ValueTask DisposeAsync()
        {
            IsTrayStarted = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProbeConfigStore : IAppConfigStore
    {
        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(AppConfig.Default);

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class ProbeModelManager : IModelManager
    {
        public IReadOnlyList<ModelOption> Catalog => [];
        public bool IsDownloaded(ModelOption model) => false;
        public InferenceStatus GetStatus() => new(false);
        public ValueTask DownloadAsync(
            ModelOption model,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask LoadAsync(ModelOption model, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(ModelOption model, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
