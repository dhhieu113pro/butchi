using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Butchi.Core.Platform;
using Xunit;

namespace Butchi.App.Tests;

public sealed class GeneralSettingsViewModelTests
{
    [Fact]
    public async Task Loads_existing_general_settings_and_autosaves_each_change()
    {
        var initial = AppConfig.Default with
        {
            TargetLanguage = "Japanese",
            FavoriteLanguages = ["Japanese", "English"],
            PopoverHideSeconds = 8
        };
        var store = new FakeConfigStore(initial);
        var vm = await GeneralSettingsViewModel.CreateAsync(
            store,
            new FakeAutoStartService(initial.LaunchAtLogin),
            CancellationToken.None);

        Assert.Equal("Japanese", vm.TargetLanguage);
        Assert.Equal(new[] { "Japanese", "English" }, vm.FavoriteLanguages);
        Assert.Equal(8u, vm.PopoverHideSeconds);
        Assert.Equal("Saved", vm.SaveStatus);

        await vm.SetThemeAsync(AppThemePreference.Dark, CancellationToken.None);
        await vm.SetTranslateEnabledAsync(false, CancellationToken.None);
        await vm.SetRewriteEnabledAsync(false, CancellationToken.None);
        await vm.SetTargetLanguageAsync("English", CancellationToken.None);
        await vm.SetFavoriteLanguagesAsync(["English", "Vietnamese"], CancellationToken.None);
        await vm.SetResultActionAsync(ResultAction.Replace, CancellationToken.None);
        await vm.SetPopoverHideSecondsAsync(12, CancellationToken.None);

        Assert.Equal(7, store.SaveCalls);
        Assert.Equal(AppThemePreference.Dark, store.Value.Theme);
        Assert.False(store.Value.TranslateEnabled);
        Assert.False(store.Value.RewriteEnabled);
        Assert.Equal("English", store.Value.TargetLanguage);
        Assert.Equal(new[] { "English", "Vietnamese" }, store.Value.FavoriteLanguages);
        Assert.Equal(ResultAction.Replace, store.Value.ResultAction);
        Assert.Equal(12u, store.Value.PopoverHideSeconds);
        Assert.Equal("Saved", vm.SaveStatus);
    }

    [Fact]
    public async Task Validates_target_favorites_and_popover_timeout_before_saving()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await GeneralSettingsViewModel.CreateAsync(
            store,
            new FakeAutoStartService(false),
            CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await vm.SetTargetLanguageAsync("   ", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await vm.SetFavoriteLanguagesAsync(["1", "2", "3", "4", "5", "6"], CancellationToken.None));

        await vm.SetPopoverHideSecondsAsync(1, CancellationToken.None);
        Assert.Equal(2u, store.Value.PopoverHideSeconds);

        await vm.SetPopoverHideSecondsAsync(99, CancellationToken.None);
        Assert.Equal(30u, store.Value.PopoverHideSeconds);
        Assert.Equal(2, store.SaveCalls);
    }

    [Fact]
    public async Task Creation_reconciles_persisted_launch_preference_to_actual_platform_state()
    {
        var store = new FakeConfigStore(AppConfig.Default with { LaunchAtLogin = true });
        var autoStart = new FakeAutoStartService(false);

        var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);

        Assert.False(vm.LaunchAtLogin);
        Assert.False(store.Value.LaunchAtLogin);
        Assert.Equal(1, store.SaveCalls);
        Assert.Equal(new[] { "get" }, autoStart.Calls);
    }

    [Fact]
    public async Task Enable_is_verified_before_config_is_persisted()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var autoStart = new FakeAutoStartService(false);
        var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);
        autoStart.Calls.Clear();

        await vm.SetLaunchAtLoginAsync(true, CancellationToken.None);

        Assert.Equal(new[] { "enable", "get" }, autoStart.Calls);
        Assert.True(vm.LaunchAtLogin);
        Assert.True(store.Value.LaunchAtLogin);
        Assert.Equal(1, store.SaveCalls);
        Assert.Equal("Saved", vm.SaveStatus);
    }

    [Fact]
    public async Task Disable_is_verified_before_config_is_persisted()
    {
        var store = new FakeConfigStore(AppConfig.Default with { LaunchAtLogin = true });
        var autoStart = new FakeAutoStartService(true);
        var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);
        autoStart.Calls.Clear();

        await vm.SetLaunchAtLoginAsync(false, CancellationToken.None);

        Assert.Equal(new[] { "disable", "get" }, autoStart.Calls);
        Assert.False(vm.LaunchAtLogin);
        Assert.False(store.Value.LaunchAtLogin);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task Verification_mismatch_does_not_persist_enabled_state()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var autoStart = new FakeAutoStartService(false) { RefuseStateChange = true };
        var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);
        autoStart.Calls.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await vm.SetLaunchAtLoginAsync(true, CancellationToken.None));

        Assert.False(store.Value.LaunchAtLogin);
        Assert.False(vm.LaunchAtLogin);
        Assert.Equal(0, store.SaveCalls);
        Assert.Equal(new[] { "enable", "get", "disable" }, autoStart.Calls);
        Assert.Equal("Couldn't save", vm.SaveStatus);
    }

    [Fact]
    public async Task Platform_failure_does_not_persist_and_restores_previous_state()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var autoStart = new FakeAutoStartService(false) { FailEnable = true };
        var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);
        autoStart.Calls.Clear();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await vm.SetLaunchAtLoginAsync(true, CancellationToken.None));

        Assert.Equal("enable failed", error.Message);
        Assert.False(store.Value.LaunchAtLogin);
        Assert.False(vm.LaunchAtLogin);
        Assert.Equal(new[] { "enable", "disable" }, autoStart.Calls);
        Assert.Equal("Couldn't save", vm.SaveStatus);
    }

    [Fact]
    public async Task Save_failure_compensates_platform_registration()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var autoStart = new FakeAutoStartService(false);
        var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);
        store.FailSave = true;
        autoStart.Calls.Clear();

        await Assert.ThrowsAsync<IOException>(async () =>
            await vm.SetLaunchAtLoginAsync(true, CancellationToken.None));

        Assert.False(autoStart.Enabled);
        Assert.False(store.Value.LaunchAtLogin);
        Assert.False(vm.LaunchAtLogin);
        Assert.Equal(new[] { "enable", "get", "disable" }, autoStart.Calls);
        Assert.Equal("Couldn't save", vm.SaveStatus);
    }

    [Fact]
    public async Task Successful_change_notifies_launch_at_login_property()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var autoStart = new FakeAutoStartService(false);
        var vm = await GeneralSettingsViewModel.CreateAsync(store, autoStart, CancellationToken.None);
        var properties = new List<string?>();
        vm.PropertyChanged += (_, args) => properties.Add(args.PropertyName);

        await vm.SetLaunchAtLoginAsync(true, CancellationToken.None);

        Assert.Contains(nameof(GeneralSettingsViewModel.LaunchAtLogin), properties);
    }

    private sealed class FakeConfigStore(AppConfig initial) : IAppConfigStore
    {
        public AppConfig Value { get; private set; } = initial;
        public int SaveCalls { get; private set; }
        public bool FailSave { get; set; }

        public ValueTask<AppConfig> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Value);
        }

        public ValueTask SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            if (FailSave)
                throw new IOException("save failed");
            Value = config;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAutoStartService(bool enabled) : IAutoStartService
    {
        public bool Enabled { get; private set; } = enabled;
        public bool FailEnable { get; set; }
        public bool FailDisable { get; set; }
        public bool RefuseStateChange { get; set; }
        public List<string> Calls { get; } = [];

        public ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add("get");
            return ValueTask.FromResult(Enabled);
        }

        public ValueTask EnableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add("enable");
            if (FailEnable)
                throw new InvalidOperationException("enable failed");
            if (!RefuseStateChange)
                Enabled = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add("disable");
            if (FailDisable)
                throw new InvalidOperationException("disable failed");
            if (!RefuseStateChange)
                Enabled = false;
            return ValueTask.CompletedTask;
        }
    }
}
