using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Core.Platform;
using Xunit;

namespace Butchi.App.Tests;

public sealed class StartupAutostartResilienceTests
{
    [Fact]
    public async Task General_settings_creation_survives_autostart_probe_failure()
    {
        var persisted = AppConfig.Default with { LaunchAtLogin = true };
        var store = new FakeConfigStore(persisted);

        var viewModel = await GeneralSettingsViewModel.CreateAsync(
            store,
            new ThrowingAutoStartService(),
            CancellationToken.None);

        Assert.True(viewModel.LaunchAtLogin);
        Assert.Equal(0, store.SaveCalls);
    }

    private sealed class FakeConfigStore(AppConfig initial) : IAppConfigStore
    {
        public AppConfig Value { get; private set; } = initial;
        public int SaveCalls { get; private set; }

        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Value);
        }

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            Value = config;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAutoStartService : IAutoStartService
    {
        public ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("startup-task probe failed");
        }

        public ValueTask EnableAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisableAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
