using Butchi.App.Settings;
using Butchi.Core.Configuration;
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
        var vm = await GeneralSettingsViewModel.CreateAsync(store, CancellationToken.None);

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
        var vm = await GeneralSettingsViewModel.CreateAsync(store, CancellationToken.None);

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
