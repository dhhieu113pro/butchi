using Butchi.App.Popover;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;
using Xunit;

namespace Butchi.App.Tests;

public sealed class PopoverViewModelTests
{
    [Theory]
    [InlineData(TextAction.Translate)]
    [InlineData(TextAction.Rewrite)]
    public void Selecting_action_raises_action_request(TextAction action)
    {
        var vm = new PopoverViewModel();
        TextAction? requested = null;
        vm.ActionRequested += (_, value) => requested = value;

        vm.SelectAction(action);

        Assert.Equal(action, vm.SelectedAction);
        Assert.Equal(action, requested);
    }

    [Theory]
    [InlineData(TextAction.Translate)]
    [InlineData(TextAction.Rewrite)]
    public void Selecting_action_enters_compact_state_before_action_request(TextAction action)
    {
        var vm = new PopoverViewModel();
        vm.SetSession("new source", action, "Vietnamese");
        bool? compactWhenRequested = null;
        vm.ActionRequested += (_, _) => compactWhenRequested = vm.IsCompact;

        vm.SelectAction(action);

        Assert.True(compactWhenRequested);
        Assert.True(vm.IsCompact);
    }

    [Fact]
    public void New_selection_session_clears_previous_results_and_invalidates_old_runs()
    {
        var vm = new PopoverViewModel();
        vm.Begin(TextAction.Translate, 7);
        vm.Append(TextAction.Translate, 7, "old translation");
        vm.FlushPendingUpdates();
        vm.Complete(TextAction.Translate, 7);
        vm.Begin(TextAction.Rewrite, 3);
        vm.Append(TextAction.Rewrite, 3, "old rewrite");
        vm.FlushPendingUpdates();

        vm.SetSession("new source", TextAction.Translate, "English");

        Assert.Equal(string.Empty, vm.Translate.Output);
        Assert.Equal(string.Empty, vm.Rewrite.Output);
        Assert.False(vm.Translate.IsRunning);
        Assert.False(vm.Rewrite.IsRunning);
        Assert.False(vm.Append(TextAction.Translate, 7, "stale"));
        Assert.False(vm.Append(TextAction.Rewrite, 3, "stale"));
    }

    [Fact]
    public void Translate_and_rewrite_keep_independent_state()
    {
        var vm = new PopoverViewModel();

        vm.Begin(TextAction.Translate, 7);
        vm.Begin(TextAction.Rewrite, 3);
        vm.Append(TextAction.Translate, 7, "xin ");
        vm.Append(TextAction.Rewrite, 3, "hello");
        vm.FlushPendingUpdates();

        Assert.Equal("xin ", vm.Translate.Output);
        Assert.Equal("hello", vm.Rewrite.Output);
        Assert.True(vm.Translate.IsRunning);
        Assert.True(vm.Rewrite.IsRunning);
    }

    [Fact]
    public void Stale_run_updates_are_rejected()
    {
        var vm = new PopoverViewModel();
        vm.Begin(TextAction.Translate, 4);
        vm.Begin(TextAction.Translate, 5);

        Assert.False(vm.Append(TextAction.Translate, 4, "stale"));
        Assert.True(vm.Append(TextAction.Translate, 5, "current"));
        vm.FlushPendingUpdates();

        Assert.Equal("current", vm.Translate.Output);
        Assert.Equal(5, vm.Translate.RunId);
    }

    [Fact]
    public void Streaming_chunks_are_batched_until_flush()
    {
        var vm = new PopoverViewModel();
        var outputNotifications = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PopoverViewModel.Translate))
                outputNotifications++;
        };

        vm.Begin(TextAction.Translate, 1);
        outputNotifications = 0;
        vm.Append(TextAction.Translate, 1, "a");
        vm.Append(TextAction.Translate, 1, "b");
        vm.Append(TextAction.Translate, 1, "c");

        Assert.Equal(string.Empty, vm.Translate.Output);
        Assert.Equal(0, outputNotifications);

        vm.FlushPendingUpdates();

        Assert.Equal("abc", vm.Translate.Output);
        Assert.Equal(1, outputNotifications);
    }

    [Fact]
    public void Favorite_language_requests_translate_rerun()
    {
        var vm = new PopoverViewModel();
        string? requested = null;
        vm.TranslateLanguageRequested += (_, language) => requested = language;

        vm.RequestFavoriteLanguage("Vietnamese");

        Assert.Equal("Vietnamese", requested);
    }

    [Fact]
    public void Auto_hide_can_be_armed_and_cancelled()
    {
        var vm = new PopoverViewModel { AutoHideDelay = TimeSpan.FromSeconds(3) };

        vm.ArmAutoHide();
        Assert.True(vm.IsAutoHideArmed);
        Assert.Equal(TimeSpan.FromSeconds(3), vm.AutoHideDelay);

        vm.CancelAutoHide();
        Assert.False(vm.IsAutoHideArmed);
    }

    [Fact]
    public void Single_translate_session_selects_translate_and_requests_auto_run()
    {
        var vm = new PopoverViewModel();
        var config = AppConfig.Default with { TranslateEnabled = true, RewriteEnabled = false };

        var autoAction = vm.SetSession("hello", config);

        Assert.True(vm.TranslateEnabled);
        Assert.False(vm.RewriteEnabled);
        Assert.Equal(TextAction.Translate, vm.SelectedAction);
        Assert.Equal(TextAction.Translate, autoAction);
    }

    [Fact]
    public void Single_rewrite_session_selects_rewrite_and_requests_auto_run()
    {
        var vm = new PopoverViewModel();
        var config = AppConfig.Default with { TranslateEnabled = false, RewriteEnabled = true };

        var autoAction = vm.SetSession("hello", config);

        Assert.False(vm.TranslateEnabled);
        Assert.True(vm.RewriteEnabled);
        Assert.Equal(TextAction.Rewrite, vm.SelectedAction);
        Assert.Equal(TextAction.Rewrite, autoAction);
    }

    [Fact]
    public void Two_enabled_actions_auto_run_translate_by_default()
    {
        var vm = new PopoverViewModel();

        var autoAction = vm.SetSession("hello", AppConfig.Default);

        Assert.True(vm.TranslateEnabled);
        Assert.True(vm.RewriteEnabled);
        Assert.Equal(TextAction.Translate, vm.SelectedAction);
        Assert.Equal(TextAction.Translate, autoAction);
    }

    [Fact]
    public void Disabled_action_cannot_be_selected_or_requested()
    {
        var vm = new PopoverViewModel();
        vm.SetSession("hello", AppConfig.Default with { TranslateEnabled = false, RewriteEnabled = true });
        TextAction? requested = null;
        vm.ActionRequested += (_, action) => requested = action;

        vm.SelectAction(TextAction.Translate);

        Assert.Equal(TextAction.Rewrite, vm.SelectedAction);
        Assert.Null(requested);
    }

    [Fact]
    public void Source_preview_collapses_embedded_newlines_and_whitespace_to_one_line()
    {
        var vm = new PopoverViewModel();
        vm.SetSession("alpha\r\n  beta\tgamma\n\n delta", TextAction.Translate, "Vietnamese");
        var previewProperty = typeof(PopoverViewModel).GetProperty("SourcePreviewText");
        var expandedProperty = typeof(PopoverViewModel).GetProperty("IsSourceExpanded");

        Assert.NotNull(previewProperty);
        Assert.NotNull(expandedProperty);
        Assert.False((bool)expandedProperty.GetValue(vm)!);
        Assert.Equal("alpha beta gamma delta", previewProperty.GetValue(vm));
    }

    [Fact]
    public void Source_preview_can_expand_to_original_text_and_collapse_again()
    {
        const string source = "line one\r\n  line two\nline three";
        var vm = new PopoverViewModel();
        vm.SetSession(source, TextAction.Translate, "Vietnamese");
        var previewProperty = typeof(PopoverViewModel).GetProperty("SourcePreviewText");
        var expandedProperty = typeof(PopoverViewModel).GetProperty("IsSourceExpanded");
        var toggleMethod = typeof(PopoverViewModel).GetMethod("RequestToggleSource");

        Assert.NotNull(previewProperty);
        Assert.NotNull(expandedProperty);
        Assert.NotNull(toggleMethod);

        toggleMethod.Invoke(vm, null);
        Assert.True((bool)expandedProperty.GetValue(vm)!);
        Assert.Equal(source, previewProperty.GetValue(vm));

        toggleMethod.Invoke(vm, null);
        Assert.False((bool)expandedProperty.GetValue(vm)!);
        Assert.Equal("line one line two line three", previewProperty.GetValue(vm));
    }

    [Fact]
    public void New_selection_session_resets_source_preview_to_collapsed()
    {
        var vm = new PopoverViewModel();
        vm.SetSession("first source", TextAction.Translate, "Vietnamese");
        var expandedProperty = typeof(PopoverViewModel).GetProperty("IsSourceExpanded");
        var toggleMethod = typeof(PopoverViewModel).GetMethod("RequestToggleSource");

        Assert.NotNull(expandedProperty);
        Assert.NotNull(toggleMethod);
        toggleMethod.Invoke(vm, null);
        Assert.True((bool)expandedProperty.GetValue(vm)!);

        vm.SetSession("second source", TextAction.Translate, "Vietnamese");

        Assert.False((bool)expandedProperty.GetValue(vm)!);
    }
}
