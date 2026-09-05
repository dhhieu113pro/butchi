using System.Reflection;
using Butchi.App.Popover;
using Xunit;

namespace Butchi.App.Tests;

public sealed class PopoverWindowPolicyTests
{
    [Fact]
    public void Window_profile_is_borderless_topmost_and_hidden_from_taskbar()
    {
        var profile = PopoverWindowProfile.Default;

        Assert.True(profile.Borderless);
        Assert.True(profile.Topmost);
        Assert.False(profile.ShowInTaskbar);
        Assert.False(profile.CanResize);
        Assert.True(profile.UseBoundedScroll);
    }

    [Fact]
    public void Escape_requests_hide_without_destroying_window()
    {
        var controller = new PopoverWindowController();
        controller.Show();

        controller.HandleEscape();

        Assert.False(controller.IsVisible);
        Assert.False(controller.IsDisposed);
    }

    [Fact]
    public void Window_deactivation_requests_immediate_hide_without_destroying_window()
    {
        var controller = new PopoverWindowController();
        controller.Show();
        controller.HandleWorkStarted();

        controller.HandleDeactivated();

        Assert.False(controller.IsVisible);
        Assert.False(controller.IsDisposed);
    }

    [Fact]
    public async Task Pinned_popover_ignores_deactivation_and_inactivity_until_unpinned()
    {
        var controller = new PopoverWindowController();
        controller.Show();
        var type = typeof(PopoverWindowController);
        var togglePinned = type.GetMethod("TogglePinned");
        var isPinned = type.GetProperty("IsPinned");

        Assert.NotNull(togglePinned);
        Assert.NotNull(isPinned);

        togglePinned.Invoke(controller, null);

        Assert.True((bool)isPinned.GetValue(controller)!);
        Assert.False(controller.HandleDeactivated());
        Assert.True(controller.IsVisible);
        Assert.False(await controller.HandlePointerExitedAsync(TimeSpan.FromMilliseconds(10)));
        Assert.True(controller.IsVisible);
        Assert.False(await controller.HandleResultCompletedAsync(TimeSpan.FromMilliseconds(10)));
        Assert.True(controller.IsVisible);

        togglePinned.Invoke(controller, null);

        Assert.False((bool)isPinned.GetValue(controller)!);
        Assert.True(controller.HandleDeactivated());
        Assert.False(controller.IsVisible);
    }

    [Fact]
    public void Escape_still_closes_a_pinned_popover()
    {
        var controller = new PopoverWindowController();
        controller.Show();
        var togglePinned = typeof(PopoverWindowController).GetMethod("TogglePinned");

        Assert.NotNull(togglePinned);
        togglePinned.Invoke(controller, null);

        Assert.True(controller.HandleEscape());
        Assert.False(controller.IsVisible);
    }

    [Fact]
    public void Same_controller_instance_is_reused_across_show_hide_cycles()
    {
        var controller = new PopoverWindowController();
        var firstIdentity = controller.InstanceId;

        controller.Show();
        controller.Hide();
        controller.Show();

        Assert.Equal(firstIdentity, controller.InstanceId);
        Assert.True(controller.IsVisible);
    }

    [Fact]
    public async Task Pointer_exit_hides_after_the_grace_period()
    {
        var controller = new PopoverWindowController();
        controller.Show();

        var hideTask = controller.HandlePointerExitedAsync(TimeSpan.FromMilliseconds(25));

        Assert.True(controller.IsVisible);
        Assert.True(await hideTask);
        Assert.False(controller.IsVisible);
    }

    [Fact]
    public async Task Pointer_reentry_cancels_the_pending_hide()
    {
        var controller = new PopoverWindowController();
        controller.Show();
        var hideTask = controller.HandlePointerExitedAsync(TimeSpan.FromSeconds(1));

        await Task.Delay(15);
        controller.HandlePointerEntered();

        Assert.False(await hideTask);
        Assert.True(controller.IsVisible);
    }

    [Fact]
    public void Default_inactivity_delays_match_the_approved_behavior()
    {
        var pointerDelay = typeof(PopoverWindowController).GetField(
            "DefaultPointerExitDelay",
            BindingFlags.NonPublic | BindingFlags.Static);
        var resultDelay = typeof(PopoverWindowController).GetField(
            "DefaultResultIdleDelay",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(pointerDelay);
        Assert.NotNull(resultDelay);
        Assert.Equal(TimeSpan.FromSeconds(3), pointerDelay.GetValue(null));
        Assert.Equal(TimeSpan.FromSeconds(8), resultDelay.GetValue(null));
    }

    [Fact]
    public async Task Completed_result_hides_after_idle_period_when_untouched()
    {
        var controller = new PopoverWindowController();
        controller.Show();
        var method = typeof(PopoverWindowController).GetMethod("HandleResultCompletedAsync");

        Assert.NotNull(method);
        var hideTask = (Task<bool>)method.Invoke(
            controller,
            new object?[] { TimeSpan.FromMilliseconds(25) })!;

        Assert.True(controller.IsVisible);
        Assert.True(await hideTask);
        Assert.False(controller.IsVisible);
    }

    [Fact]
    public async Task Pointer_enter_cancels_result_idle_hide()
    {
        var controller = new PopoverWindowController();
        controller.Show();
        var method = typeof(PopoverWindowController).GetMethod("HandleResultCompletedAsync");

        Assert.NotNull(method);
        var hideTask = (Task<bool>)method.Invoke(
            controller,
            new object?[] { TimeSpan.FromSeconds(1) })!;

        await Task.Delay(15);
        controller.HandlePointerEntered();

        Assert.False(await hideTask);
        Assert.True(controller.IsVisible);
    }

    [Fact]
    public async Task Result_does_not_auto_hide_while_pointer_is_inside()
    {
        var controller = new PopoverWindowController();
        controller.Show();
        controller.HandlePointerEntered();
        var method = typeof(PopoverWindowController).GetMethod("HandleResultCompletedAsync");

        Assert.NotNull(method);
        var hideTask = (Task<bool>)method.Invoke(
            controller,
            new object?[] { TimeSpan.FromMilliseconds(25) })!;

        Assert.False(await hideTask);
        Assert.True(controller.IsVisible);
    }

    [Fact]
    public async Task Popover_never_hides_from_pointer_exit_while_work_is_running()
    {
        var controller = new PopoverWindowController();
        controller.Show();
        var method = typeof(PopoverWindowController).GetMethod("HandleWorkStarted");

        Assert.NotNull(method);
        method.Invoke(controller, null);

        var hidden = await controller.HandlePointerExitedAsync(TimeSpan.FromMilliseconds(25));

        Assert.False(hidden);
        Assert.True(controller.IsVisible);
    }

    [Fact]
    public async Task Starting_new_work_cancels_a_pending_result_idle_hide()
    {
        var controller = new PopoverWindowController();
        controller.Show();
        var completeMethod = typeof(PopoverWindowController).GetMethod("HandleResultCompletedAsync");
        var startMethod = typeof(PopoverWindowController).GetMethod("HandleWorkStarted");

        Assert.NotNull(completeMethod);
        Assert.NotNull(startMethod);
        var hideTask = (Task<bool>)completeMethod.Invoke(
            controller,
            new object?[] { TimeSpan.FromSeconds(1) })!;

        await Task.Delay(15);
        startMethod.Invoke(controller, null);

        Assert.False(await hideTask);
        Assert.True(controller.IsVisible);
    }

    [Theory]
    [InlineData(PopoverTheme.System, "Default")]
    [InlineData(PopoverTheme.Light, "Light")]
    [InlineData(PopoverTheme.Dark, "Dark")]
    public void Theme_policy_maps_to_Avalonia_variant(PopoverTheme theme, string expected)
    {
        Assert.Equal(expected, PopoverThemePolicy.ToVariantName(theme));
    }
}
