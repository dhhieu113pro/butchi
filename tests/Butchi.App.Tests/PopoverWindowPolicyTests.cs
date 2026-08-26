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

    [Theory]
    [InlineData(PopoverTheme.System, "Default")]
    [InlineData(PopoverTheme.Light, "Light")]
    [InlineData(PopoverTheme.Dark, "Dark")]
    public void Theme_policy_maps_to_Avalonia_variant(PopoverTheme theme, string expected)
    {
        Assert.Equal(expected, PopoverThemePolicy.ToVariantName(theme));
    }
}
