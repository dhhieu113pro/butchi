using Butchi.App.Settings;
using Butchi.Core.Configuration;
using Xunit;

namespace Butchi.App.Tests;

public sealed class PromptsViewModelTests
{
    [Fact]
    public async Task Loads_translate_and_rewrite_profiles_and_switches_modes_without_saving()
    {
        var initial = AppConfig.Default with
        {
            TranslateSystemPrompt = "Custom translate prompt",
            RewriteSystemPrompt = AppConfig.Default.RewriteSystemPrompt
        };
        var store = new FakeConfigStore(initial);
        var vm = await PromptsViewModel.CreateAsync(store, CancellationToken.None);

        Assert.Equal(PromptMode.Translate, vm.Mode);
        Assert.Contains(vm.Profiles, profile => profile.Name == "Balanced");
        Assert.Contains(vm.Profiles, profile => profile.Name == "Literal");
        Assert.Contains(vm.Profiles, profile => profile.Name == "Natural");
        Assert.Contains(vm.Profiles, profile => profile.Name == "Custom");
        Assert.Equal("Custom", vm.SelectedProfile.Name);
        Assert.Equal("Custom translate prompt", vm.PromptText);

        vm.SetMode(PromptMode.Rewrite);

        Assert.Equal(PromptMode.Rewrite, vm.Mode);
        Assert.Contains(vm.Profiles, profile => profile.Name == "Balanced");
        Assert.Contains(vm.Profiles, profile => profile.Name == "Concise");
        Assert.Contains(vm.Profiles, profile => profile.Name == "Polished");
        Assert.Contains(vm.Profiles, profile => profile.Name == "Custom");
        Assert.Equal("Balanced", vm.SelectedProfile.Name);
        Assert.Equal(AppConfig.Default.RewriteSystemPrompt, vm.PromptText);
        Assert.Equal(0, store.SaveCalls);
    }

    [Fact]
    public async Task Selecting_a_preset_persists_its_prompt_for_the_active_mode()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await PromptsViewModel.CreateAsync(store, CancellationToken.None);

        var literal = vm.Profiles.Single(profile => profile.Name == "Literal");
        await vm.SetProfileAsync(literal.Name, CancellationToken.None);

        Assert.Equal("Literal", vm.SelectedProfile.Name);
        Assert.Equal(literal.Prompt, vm.PromptText);
        Assert.Equal(literal.Prompt, store.Value.TranslateSystemPrompt);
        Assert.Equal(AppConfig.Default.RewriteSystemPrompt, store.Value.RewriteSystemPrompt);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task Editing_preset_text_transitions_to_custom_and_persists_only_active_prompt()
    {
        var store = new FakeConfigStore(AppConfig.Default);
        var vm = await PromptsViewModel.CreateAsync(store, CancellationToken.None);

        Assert.Equal("Balanced", vm.SelectedProfile.Name);

        await vm.SetPromptTextAsync("Translate naturally but preserve product names.", CancellationToken.None);

        Assert.Equal("Custom", vm.SelectedProfile.Name);
        Assert.Equal("Translate naturally but preserve product names.", vm.PromptText);
        Assert.Equal("Translate naturally but preserve product names.", store.Value.TranslateSystemPrompt);
        Assert.Equal(AppConfig.Default.RewriteSystemPrompt, store.Value.RewriteSystemPrompt);

        vm.SetMode(PromptMode.Rewrite);
        await vm.SetPromptTextAsync("Rewrite with a concise professional tone.", CancellationToken.None);

        Assert.Equal("Custom", vm.SelectedProfile.Name);
        Assert.Equal("Rewrite with a concise professional tone.", store.Value.RewriteSystemPrompt);
        Assert.Equal("Translate naturally but preserve product names.", store.Value.TranslateSystemPrompt);
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
