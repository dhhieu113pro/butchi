using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.Diagnostics;
using Butchi.App.Management;
using Butchi.App.Popover;
using Butchi.App.Startup;
using Butchi.App.Styling;
using Butchi.App.Tray;
using Butchi.Core.Configuration;

namespace Butchi.App;

public sealed class App : Application, IApplicationShutdown
{
    private CancellationTokenSource? _shutdownCts;
    private StartupApplicationServices? _services;
    private StartupCoordinator? _coordinator;
    private Task? _startupTask;
    private int _shutdownStarted;

    public PopoverWindow? PopoverWindow => (_coordinator?.Runtime as ButchiRuntime)?.PopoverWindow;
    public ManagementWindow? ManagementWindow => (_coordinator?.Runtime as ButchiRuntime)?.ManagementWindow;
    public TrayCommandRouter? TrayRouter => (_coordinator?.Runtime as ButchiRuntime)?.TrayRouter;

    public override void OnFrameworkInitializationCompleted()
    {
        ButchiTheme.Initialize(this);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        base.OnFrameworkInitializationCompleted();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            _shutdownCts = new CancellationTokenSource();
            _startupTask = StartAsync(_shutdownCts.Token);
        }
    }

    public void Shutdown() => _ = ShutdownAsync();

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _services = new StartupApplicationServices();
            var runtimeFactory = new ButchiRuntimeFactory(this, _services, this);
            if (await new ScreenshotStartup(runtimeFactory).TryRunAsync(Program.StartupArgs, cancellationToken))
            {
                Shutdown();
                return;
            }

            _coordinator = new StartupCoordinator(
                new StartupReadinessService(_services.ConfigStore, _services.ModelManager),
                new WelcomeSetupViewModelFactory(_services.ConfigStore, _services.ModelManager),
                new WelcomeSetupHost(),
                runtimeFactory,
                Shutdown);
            await _coordinator.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_services is null)
            {
                Shutdown();
                return;
            }

            var failure = new StartupReadinessResult(
                false,
                AppConfig.Default,
                StartupReadinessReason.RuntimeFailed,
                exception.GetType().Name);
            _coordinator = new StartupCoordinator(
                new FixedReadinessService(failure),
                new WelcomeSetupViewModelFactory(_services.ConfigStore, _services.ModelManager),
                new WelcomeSetupHost(),
                new ButchiRuntimeFactory(this, _services, this),
                Shutdown);

            try
            {
                await _coordinator.RunAsync(cancellationToken);
            }
            catch
            {
                Shutdown();
            }
        }
    }

    private async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
        try
        {
            _shutdownCts?.Cancel();
            if (_startupTask is not null)
                await _startupTask;
            if (_coordinator is not null)
                await _coordinator.DisposeAsync();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Butchi shutdown coordination failed: {exception.GetType().Name}");
        }
        finally
        {
            try
            {
                if (_services is not null)
                    await _services.DisposeAsync();
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Butchi service cleanup failed: {exception.GetType().Name}");
            }
            finally
            {
                _shutdownCts?.Dispose();
                _shutdownCts = null;
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            }
        }
    }

    private sealed class FixedReadinessService(StartupReadinessResult result) : IStartupReadinessService
    {
        public ValueTask<StartupReadinessResult> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);
    }
}
