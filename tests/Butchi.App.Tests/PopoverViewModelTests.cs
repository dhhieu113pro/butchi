using Butchi.App.Popover;
using Butchi.Core.Actions;
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
}
