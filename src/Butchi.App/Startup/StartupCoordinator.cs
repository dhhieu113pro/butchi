namespace Butchi.App.Startup;

public enum StartupCoordinatorState
{
    NotStarted,
    Checking,
    ShowingSetup,
    StartingRuntime,
    Running,
    Exiting
}

public sealed class StartupCoordinator(
    IStartupReadinessService readiness,
    IWelcomeSetupViewModelFactory welcomeViewModelFactory,
    IWelcomeSetupHost welcomeHost,
    IButchiRuntimeFactory runtimeFactory,
    Action shutdown) : IAsyncDisposable
{
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private IButchiRuntime? _runtime;
    private int _disposed;

    public StartupCoordinatorState State { get; private set; } = StartupCoordinatorState.NotStarted;
    public IButchiRuntime? Runtime => _runtime;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            if (State != StartupCoordinatorState.NotStarted)
                return;

            State = StartupCoordinatorState.Checking;
            var result = await readiness.CheckAsync(cancellationToken);
            while (true)
            {
                var config = result.Config;
                if (!result.IsReady)
                {
                    State = StartupCoordinatorState.ShowingSetup;
                    var completion = await welcomeHost.ShowAsync(
                        welcomeViewModelFactory.Create(result),
                        cancellationToken);
                    if (completion is null)
                    {
                        State = StartupCoordinatorState.Exiting;
                        shutdown();
                        return;
                    }
                    config = completion.Config;
                }

                try
                {
                    State = StartupCoordinatorState.StartingRuntime;
                    _runtime = await runtimeFactory.CreateAsync(config, cancellationToken);
                    _runtime.StartTray();
                    State = StartupCoordinatorState.Running;
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (_runtime is not null)
                    {
                        await _runtime.DisposeAsync();
                        _runtime = null;
                    }

                    result = new StartupReadinessResult(
                        false,
                        config,
                        StartupReadinessReason.RuntimeFailed,
                        exception.GetType().Name);
                }
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _runGate.WaitAsync();
        try
        {
            State = StartupCoordinatorState.Exiting;
            if (_runtime is not null)
            {
                await _runtime.DisposeAsync();
                _runtime = null;
            }
        }
        finally
        {
            _runGate.Release();
            _runGate.Dispose();
        }
    }
}
