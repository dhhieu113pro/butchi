using Butchi.App.Popover;
using Butchi.Core.Actions;
using Xunit;

namespace Butchi.App.Tests;

public sealed class PopoverParityViewModelTests
{
    [Fact]
    public void Session_tracks_selected_action_source_and_target_language()
    {
        var vm = new PopoverViewModel();

        vm.SetSession("Good morning", TextAction.Translate, "Vietnamese");

        Assert.Equal("Good morning", vm.SourceText);
        Assert.Equal(TextAction.Translate, vm.SelectedAction);
        Assert.Equal("Vietnamese", vm.TargetLanguage);
    }

    [Fact]
    public void Complete_and_fail_expose_success_and_error_states_for_the_current_run()
    {
        var vm = new PopoverViewModel();
        vm.Begin(TextAction.Translate, 7);
        vm.Append(TextAction.Translate, 7, "Xin chào");
        vm.FlushPendingUpdates();

        Assert.True(vm.Complete(TextAction.Translate, 7));
        Assert.False(vm.Translate.IsRunning);
        Assert.Null(vm.Translate.ErrorMessage);

        vm.Begin(TextAction.Rewrite, 8);
        Assert.True(vm.Fail(TextAction.Rewrite, 8, "Model unavailable"));
        Assert.False(vm.Rewrite.IsRunning);
        Assert.Equal("Model unavailable", vm.Rewrite.ErrorMessage);
    }

    [Fact]
    public void Compact_presentation_is_used_only_while_initial_inference_has_no_output()
    {
        var property = typeof(PopoverViewModel).GetProperty("IsCompact");
        Assert.NotNull(property);

        var vm = new PopoverViewModel();
        vm.SetSession("source", TextAction.Translate, "Vietnamese");

        Assert.False((bool)property.GetValue(vm)!);

        vm.Begin(TextAction.Translate, 10);
        Assert.True((bool)property.GetValue(vm)!);

        vm.Append(TextAction.Translate, 10, "Xin");
        vm.FlushPendingUpdates();
        Assert.False((bool)property.GetValue(vm)!);

        vm.Begin(TextAction.Translate, 11);
        Assert.True((bool)property.GetValue(vm)!);
        vm.Fail(TextAction.Translate, 11, "Model unavailable");
        Assert.False((bool)property.GetValue(vm)!);
    }

    [Fact]
    public void Rerun_copy_and_replace_actions_delegate_current_result()
    {
        var vm = new PopoverViewModel();
        vm.SetSession("source", TextAction.Rewrite, null);
        vm.Begin(TextAction.Rewrite, 9);
        vm.Append(TextAction.Rewrite, 9, "result");
        vm.FlushPendingUpdates();
        vm.Complete(TextAction.Rewrite, 9);

        TextAction? rerun = null;
        string? copied = null;
        string? replaced = null;
        vm.RerunRequested += (_, action) => rerun = action;
        vm.CopyRequested += (_, text) => copied = text;
        vm.ReplaceRequested += (_, text) => replaced = text;

        vm.RequestRerun();
        vm.RequestCopy();
        vm.RequestReplace();

        Assert.Equal(TextAction.Rewrite, rerun);
        Assert.Equal("result", copied);
        Assert.Equal("result", replaced);
    }
}
