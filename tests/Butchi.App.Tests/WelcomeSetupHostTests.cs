using Butchi.App.Models;
using Butchi.App.Settings;
using Butchi.App.Startup;
using Butchi.Core.Configuration;
using Butchi.Core.Inference;
using Butchi.Inference;
using Xunit;

namespace Butchi.App.Tests;

public sealed class WelcomeSetupHostTests
{
    [Fact]
    public async Task Show_waits_until_the_surface_completes_setup()
    {
        var surface = new FakeWelcomeSetupSurface();
        var host = new WelcomeSetupHost(_ => surface);
        var pending = host.ShowAsync(CreateViewModel(), CancellationToken.None).AsTask();

        Assert.False(pending.IsCompleted);
        Assert.Equal(1, surface.ShowCalls);

        var completion = new WelcomeSetupCompletion(AppConfig.Default with { TargetLanguage = "Japanese" });
        surface.Complete(completion);

        Assert.Equal("Japanese", (await pending)?.Config.TargetLanguage);
        Assert.Equal(1, surface.CloseAfterCompletionCalls);
    }

    [Fact]
    public async Task Closing_setup_before_completion_returns_exit()
    {
        var surface = new FakeWelcomeSetupSurface();
        var host = new WelcomeSetupHost(_ => surface);
        var pending = host.ShowAsync(CreateViewModel(), CancellationToken.None).AsTask();

        surface.Exit();

        Assert.Null(await pending);
    }

    [Fact]
    public async Task Cancellation_closes_setup_and_propagates_cancellation()
    {
        var surface = new FakeWelcomeSetupSurface();
        var host = new WelcomeSetupHost(_ => surface);
        using var cancellation = new CancellationTokenSource();
        var pending = host.ShowAsync(CreateViewModel(), cancellation.Token).AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, surface.CloseAfterCompletionCalls);
    }

    [Fact]
    public async Task Exit_cancels_and_waits_for_active_setup_operation()
    {
        var surface = new CancellableWelcomeSetupSurface();
        var host = new WelcomeSetupHost(_ => surface);
        var pending = host.ShowAsync(CreateViewModel(), CancellationToken.None).AsTask();

        surface.Exit();

        Assert.False(pending.IsCompleted);
        Assert.True(surface.OperationToken.IsCancellationRequested);
        surface.FinishCancellation();
        Assert.Null(await pending);
    }

    private static WelcomeSetupViewModel CreateViewModel() =>
        new(
            new StartupReadinessResult(false, AppConfig.Default, StartupReadinessReason.SettingsMissing),
            new NoOpConfigStore(),
            new EmptyModelManager());

    private sealed class FakeWelcomeSetupSurface : IWelcomeSetupSurface
    {
        public event Action<WelcomeSetupCompletion>? Completed;
        public event Action? ExitRequested;
        public int ShowCalls { get; private set; }
        public int CloseAfterCompletionCalls { get; private set; }

        public void Show() => ShowCalls++;
        public void CloseAfterCompletion() => CloseAfterCompletionCalls++;
        public void Complete(WelcomeSetupCompletion completion) => Completed?.Invoke(completion);
        public void Exit() => ExitRequested?.Invoke();
    }

    private sealed class CancellableWelcomeSetupSurface : IWelcomeSetupSurface, ICancellableWelcomeSetupSurface
    {
        private readonly TaskCompletionSource _operation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenSource? _operationCancellation;

        public event Action<WelcomeSetupCompletion>? Completed;
        public event Action? ExitRequested;
        public CancellationToken OperationToken { get; private set; }

        public void SetOperationCancellation(CancellationToken cancellationToken)
        {
            _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            OperationToken = _operationCancellation.Token;
        }

        public void CancelActiveOperation() => _operationCancellation?.Cancel();

        public async ValueTask WaitForActiveOperationAsync()
        {
            await _operation.Task;
            _operationCancellation?.Dispose();
        }

        public void Show() { }
        public void CloseAfterCompletion() { }
        public void Complete(WelcomeSetupCompletion completion) => Completed?.Invoke(completion);
        public void Exit() => ExitRequested?.Invoke();
        public void FinishCancellation() => _operation.TrySetResult();
    }

    private sealed class NoOpConfigStore : IAppConfigStore
    {
        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(AppConfig.Default);
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
