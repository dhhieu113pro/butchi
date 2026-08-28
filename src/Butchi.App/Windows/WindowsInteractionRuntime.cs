using System.Diagnostics;
using Butchi.Platform.Windows.Triggers;

namespace Butchi.App.Windows;

public sealed class WindowsInteractionRuntime(
    WindowsTriggerService triggerService,
    WindowsActivationCoordinator activationCoordinator) : IDisposable
{
    private int _started;
    private int _disposed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        triggerService.Triggered += OnTriggered;
        try
        {
            triggerService.Start();
        }
        catch
        {
            triggerService.Triggered -= OnTriggered;
            Interlocked.Exchange(ref _started, 0);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        triggerService.Triggered -= OnTriggered;
        triggerService.Dispose();
    }

    private void OnTriggered(object? sender, EventArgs e) => _ = ActivateAsync();

    private async Task ActivateAsync()
    {
        try
        {
            await activationCoordinator.ActivateAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Butchi Windows selection activation failed: {exception.GetType().Name}");
        }
    }
}
