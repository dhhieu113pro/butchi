using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Xunit;

namespace Butchi.App.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task Edits_do_not_save_until_save_is_requested()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await SettingsViewModel.CreateAsync(store, CancellationToken.None);

        vm.WorkingCopy = vm.WorkingCopy with { TargetLanguage = "English" };

        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal(0, store.SaveCalls);

        await vm.SaveAsync(CancellationToken.None);

        Assert.Equal(1, store.SaveCalls);
        Assert.Equal("English", store.Value.TargetLanguage);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task Reset_and_restore_defaults_do_not_save_automatically()
    {
        var persisted = AppConfig.Default with { TargetLanguage = "Japanese" };
        var store = new FakeConfigStore(persisted);
        var vm = await SettingsViewModel.CreateAsync(store, CancellationToken.None);

        vm.WorkingCopy = vm.WorkingCopy with { TargetLanguage = "English" };
        await vm.ResetAsync(CancellationToken.None);
        Assert.Equal("Japanese", vm.WorkingCopy.TargetLanguage);

        vm.RestoreDefaults();
        Assert.Equal(AppConfig.Default.TargetLanguage, vm.WorkingCopy.TargetLanguage);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task Live_setting_change_does_not_require_model_reload()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await SettingsViewModel.CreateAsync(store, CancellationToken.None);

        vm.WorkingCopy = vm.WorkingCopy with { TargetLanguage = "English" };
        await vm.SaveAsync(CancellationToken.None);

        Assert.False(vm.RequiresModelReload);
    }

    [Fact]
    public async Task Model_or_backend_change_requires_model_reload_after_save()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await SettingsViewModel.CreateAsync(store, CancellationToken.None);

        vm.WorkingCopy = vm.WorkingCopy with { ModelFile = "other.gguf" };
        await vm.SaveAsync(CancellationToken.None);

        Assert.True(vm.RequiresModelReload);
    }

    private sealed class FakeConfigStore(AppConfig initial) : IAppConfigStore
    {
        public AppConfig Value { get; private set; } = initial;
        public int SaveCalls { get; private set; }

        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Value);

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            SaveCalls++;
            Value = config;
            return ValueTask.CompletedTask;
        }
    }
}
