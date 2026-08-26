using Butchi.Core.Actions;
using Butchi.Core.Configuration;
using Butchi.Core.Prompts;
using Xunit;

namespace Butchi.Core.Tests;

public sealed class CoreBehaviorParityTests
{
    [Fact]
    public void Defaults_match_reference_Butchi()
    {
        var config = AppConfig.Default;

        Assert.True(config.TranslateEnabled);
        Assert.True(config.RewriteEnabled);
        Assert.Equal("Vietnamese", config.TargetLanguage);
        Assert.Equal(["Vietnamese", "English"], config.FavoriteLanguages);
        Assert.Equal(ResultAction.Copy, config.ResultAction);
        Assert.Equal(BackendPreference.Auto, config.BackendPreference);
        Assert.Equal("unsloth/Qwen3.5-0.8B-GGUF", config.ModelRepo);
        Assert.Equal("Qwen3.5-0.8B-Q4_K_M.gguf", config.ModelFile);
        Assert.Equal(256u, config.MaxTokens);
        Assert.Equal(0.3f, config.Temperature);
        Assert.Equal(999u, config.GpuLayers);
        Assert.Equal(30, config.HistoryRetentionDays);
        Assert.Equal(6u, config.PopoverHideSeconds);
    }

    [Theory]
    [InlineData(" Translate ", TextAction.Translate)]
    [InlineData("REWRITE", TextAction.Rewrite)]
    public void TextAction_parses_reference_values(string value, TextAction expected) =>
        Assert.Equal(expected, TextActionParser.Parse(value));

    [Fact]
    public void TextAction_rejects_unknown_values() =>
        Assert.Throws<ArgumentException>(() => TextActionParser.Parse("summarize"));

    [Theory]
    [InlineData("  Japanese  ", "Japanese")]
    [InlineData(" Vietnamese ", "Vietnamese")]
    public void Target_language_is_trimmed(string value, string expected) =>
        Assert.Equal(expected, AppConfig.NormalizeTargetLanguage(value));

    [Fact]
    public void Blank_target_language_is_rejected() =>
        Assert.Throws<ArgumentException>(() => AppConfig.NormalizeTargetLanguage("   "));

    [Theory]
    [InlineData(" CPU ", BackendPreference.Cpu)]
    [InlineData("GPU", BackendPreference.Gpu)]
    [InlineData("anything", BackendPreference.Auto)]
    public void Backend_preference_normalizes_like_reference(string value, BackendPreference expected) =>
        Assert.Equal(expected, BackendPreferenceParser.ParseOrAuto(value));

    [Fact]
    public void Prompt_builder_preserves_Qwen_chat_framing_and_translate_target()
    {
        var config = AppConfig.Default with { TargetLanguage = "Japanese" };
        var prompt = PromptBuilder.Build(TextAction.Translate, "hello", config);

        Assert.Contains("<|im_start|>system\n", prompt);
        Assert.Contains(config.TranslateSystemPrompt, prompt);
        Assert.Contains("Target language: Japanese.", prompt);
        Assert.Contains("<|im_start|>user\nhello<|im_end|>", prompt);
        Assert.EndsWith("<|im_start|>assistant\n", prompt);
    }

    [Fact]
    public void Rewrite_prompt_preserves_source_language_instruction_without_translate_target()
    {
        var config = AppConfig.Default;
        var prompt = PromptBuilder.Build(TextAction.Rewrite, "xin chao", config);

        Assert.Contains(config.RewriteSystemPrompt, prompt);
        Assert.DoesNotContain("Target language:", prompt);
        Assert.Contains("<|im_start|>user\nxin chao<|im_end|>", prompt);
    }

    [Fact]
    public void Enabled_actions_keep_translate_then_rewrite_order()
    {
        Assert.Equal([TextAction.Translate, TextAction.Rewrite], AutomationRules.GetEnabledActions(AppConfig.Default));
        Assert.Equal([TextAction.Rewrite], AutomationRules.GetEnabledActions(AppConfig.Default with { TranslateEnabled = false }));
    }

    [Theory]
    [InlineData(true, false, ResultAction.Copy, true)]
    [InlineData(false, true, ResultAction.Replace, true)]
    [InlineData(true, true, ResultAction.Copy, false)]
    [InlineData(true, false, ResultAction.None, false)]
    public void Automatic_result_requires_exactly_one_enabled_action_and_non_none_result(
        bool translate, bool rewrite, ResultAction resultAction, bool expected)
    {
        var config = AppConfig.Default with
        {
            TranslateEnabled = translate,
            RewriteEnabled = rewrite,
            ResultAction = resultAction
        };

        Assert.Equal(expected, AutomationRules.ShouldApplyAutomaticResult(config));
    }
}
