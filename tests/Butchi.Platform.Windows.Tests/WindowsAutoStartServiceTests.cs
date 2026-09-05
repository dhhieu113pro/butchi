using Butchi.Core.Platform;
using Butchi.Platform.Windows.AutoStart;
using Xunit;

namespace Butchi.Platform.Windows.Tests;

public sealed class WindowsAutoStartServiceTests
{
    [Fact]
    public async Task Unpackaged_service_writes_quoted_current_executable_and_rejects_foreign_value()
    {
        var store = new FakeRunKeyStore();
        var service = new WindowsRunKeyAutoStartService(
            store,
            @"C:\Program Files\Butchi\butchi.exe");

        Assert.False(await service.GetEnabledAsync(CancellationToken.None));
        await service.EnableAsync(CancellationToken.None);

        Assert.Equal("\"C:\\Program Files\\Butchi\\butchi.exe\"", store.Value);
        Assert.True(await service.GetEnabledAsync(CancellationToken.None));

        store.Value = "\"C:\\Other\\other.exe\"";
        Assert.False(await service.GetEnabledAsync(CancellationToken.None));

        await service.DisableAsync(CancellationToken.None);
        Assert.Null(store.Value);
    }

    [Theory]
    [InlineData(WindowsStartupTaskStatus.Enabled, true)]
    [InlineData(WindowsStartupTaskStatus.EnabledByPolicy, true)]
    [InlineData(WindowsStartupTaskStatus.Disabled, false)]
    [InlineData(WindowsStartupTaskStatus.DisabledByUser, false)]
    [InlineData(WindowsStartupTaskStatus.DisabledByPolicy, false)]
    public async Task Packaged_service_maps_all_startup_task_states(
        WindowsStartupTaskStatus state,
        bool expected)
    {
        var accessor = new FakeStartupTaskAccessor { State = state };
        var service = new WindowsStartupTaskAutoStartService(accessor);

        Assert.Equal(expected, await service.GetEnabledAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(WindowsStartupTaskStatus.DisabledByUser)]
    [InlineData(WindowsStartupTaskStatus.DisabledByPolicy)]
    public async Task Packaged_service_does_not_bypass_user_or_policy_disable(
        WindowsStartupTaskStatus state)
    {
        var accessor = new FakeStartupTaskAccessor { State = state };
        var service = new WindowsStartupTaskAutoStartService(accessor);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.EnableAsync(CancellationToken.None));

        Assert.Equal(0, accessor.RequestEnableCalls);
        Assert.Equal(state, accessor.State);
    }

    [Fact]
    public async Task Packaged_service_requests_enable_only_from_disabled_state()
    {
        var accessor = new FakeStartupTaskAccessor
        {
            State = WindowsStartupTaskStatus.Disabled,
            RequestedState = WindowsStartupTaskStatus.Enabled
        };
        var service = new WindowsStartupTaskAutoStartService(accessor);

        await service.EnableAsync(CancellationToken.None);

        Assert.Equal(1, accessor.RequestEnableCalls);
        Assert.True(await service.GetEnabledAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Packaged_service_rejects_enable_request_that_windows_did_not_enable()
    {
        var accessor = new FakeStartupTaskAccessor
        {
            State = WindowsStartupTaskStatus.Disabled,
            RequestedState = WindowsStartupTaskStatus.DisabledByUser
        };
        var service = new WindowsStartupTaskAutoStartService(accessor);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.EnableAsync(CancellationToken.None));

        Assert.Equal(1, accessor.RequestEnableCalls);
    }

    [Fact]
    public async Task Packaged_service_disables_through_startup_task_accessor()
    {
        var accessor = new FakeStartupTaskAccessor { State = WindowsStartupTaskStatus.Enabled };
        var service = new WindowsStartupTaskAutoStartService(accessor);

        await service.DisableAsync(CancellationToken.None);

        Assert.Equal(1, accessor.DisableCalls);
        Assert.Equal(WindowsStartupTaskStatus.Disabled, accessor.State);
    }

    [Fact]
    public async Task Dispatcher_uses_packaged_service_when_package_identity_exists()
    {
        var packaged = new FakeAutoStartService();
        var unpackaged = new FakeAutoStartService();
        var service = new WindowsAutoStartService(
            new FixedPackageIdentity(true),
            packaged,
            unpackaged);

        await service.EnableAsync(CancellationToken.None);

        Assert.Equal(1, packaged.EnableCalls);
        Assert.Equal(0, unpackaged.EnableCalls);
    }

    [Fact]
    public async Task Dispatcher_uses_run_key_service_when_package_identity_is_absent()
    {
        var packaged = new FakeAutoStartService();
        var unpackaged = new FakeAutoStartService();
        var service = new WindowsAutoStartService(
            new FixedPackageIdentity(false),
            packaged,
            unpackaged);

        await service.EnableAsync(CancellationToken.None);

        Assert.Equal(0, packaged.EnableCalls);
        Assert.Equal(1, unpackaged.EnableCalls);
    }

    private sealed class FakeRunKeyStore : IRunKeyStore
    {
        public string? Value { get; set; }

        public string? Read() => Value;
        public void Write(string command) => Value = command;
        public void Delete() => Value = null;
    }

    private sealed class FakeStartupTaskAccessor : IStartupTaskAccessor
    {
        public WindowsStartupTaskStatus State { get; set; }
        public WindowsStartupTaskStatus RequestedState { get; set; } = WindowsStartupTaskStatus.Enabled;
        public int RequestEnableCalls { get; private set; }
        public int DisableCalls { get; private set; }

        public ValueTask<WindowsStartupTaskStatus> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(State);
        }

        public ValueTask<WindowsStartupTaskStatus> RequestEnableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestEnableCalls++;
            State = RequestedState;
            return ValueTask.FromResult(State);
        }

        public ValueTask DisableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisableCalls++;
            State = WindowsStartupTaskStatus.Disabled;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedPackageIdentity(bool isPackaged) : IWindowsPackageIdentity
    {
        public bool IsPackaged => isPackaged;
    }

    private sealed class FakeAutoStartService : IAutoStartService
    {
        public int EnableCalls { get; private set; }

        public ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);

        public ValueTask EnableAsync(CancellationToken cancellationToken)
        {
            EnableCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisableAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
