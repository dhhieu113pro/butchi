using System.Runtime.Versioning;
using Butchi.Core.Platform;
using Windows.ApplicationModel;

namespace Butchi.Platform.Windows.AutoStart;

internal enum WindowsStartupTaskStatus
{
    Disabled,
    DisabledByUser,
    Enabled,
    DisabledByPolicy,
    EnabledByPolicy
}

internal interface IStartupTaskAccessor
{
    ValueTask<WindowsStartupTaskStatus> GetStateAsync(CancellationToken cancellationToken);
    ValueTask<WindowsStartupTaskStatus> RequestEnableAsync(CancellationToken cancellationToken);
    ValueTask DisableAsync(CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class WinRtStartupTaskAccessor(string taskId) : IStartupTaskAccessor
{
    public async ValueTask<WindowsStartupTaskStatus> GetStateAsync(CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(cancellationToken);
        return Map(task.State);
    }

    public async ValueTask<WindowsStartupTaskStatus> RequestEnableAsync(CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(cancellationToken);
        var state = await task.RequestEnableAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return Map(state);
    }

    public async ValueTask DisableAsync(CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        task.Disable();
    }

    private async ValueTask<StartupTask> GetTaskAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = await StartupTask.GetAsync(taskId);
        cancellationToken.ThrowIfCancellationRequested();
        return task;
    }

    private static WindowsStartupTaskStatus Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => WindowsStartupTaskStatus.Disabled,
        StartupTaskState.DisabledByUser => WindowsStartupTaskStatus.DisabledByUser,
        StartupTaskState.Enabled => WindowsStartupTaskStatus.Enabled,
        StartupTaskState.DisabledByPolicy => WindowsStartupTaskStatus.DisabledByPolicy,
        StartupTaskState.EnabledByPolicy => WindowsStartupTaskStatus.EnabledByPolicy,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown Windows startup task state.")
    };
}

internal sealed class WindowsStartupTaskAutoStartService(
    IStartupTaskAccessor accessor) : IAutoStartService
{
    public async ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
    {
        var state = await accessor.GetStateAsync(cancellationToken);
        return state is WindowsStartupTaskStatus.Enabled or WindowsStartupTaskStatus.EnabledByPolicy;
    }

    public async ValueTask EnableAsync(CancellationToken cancellationToken)
    {
        var current = await accessor.GetStateAsync(cancellationToken);
        if (current is WindowsStartupTaskStatus.Enabled or WindowsStartupTaskStatus.EnabledByPolicy)
        {
            return;
        }

        if (current is WindowsStartupTaskStatus.DisabledByUser or WindowsStartupTaskStatus.DisabledByPolicy)
        {
            throw new InvalidOperationException("Windows startup is disabled by the user or policy.");
        }

        var result = await accessor.RequestEnableAsync(cancellationToken);
        if (result is not WindowsStartupTaskStatus.Enabled and not WindowsStartupTaskStatus.EnabledByPolicy)
        {
            throw new InvalidOperationException("Windows did not enable the Butchi startup task.");
        }
    }

    public ValueTask DisableAsync(CancellationToken cancellationToken) =>
        accessor.DisableAsync(cancellationToken);
}
