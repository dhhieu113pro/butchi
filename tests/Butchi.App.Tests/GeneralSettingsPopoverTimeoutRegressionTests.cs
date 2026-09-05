using Butchi.App.Popover;
using Butchi.Core.Configuration;
using Xunit;

namespace Butchi.App.Tests;

public sealed class GeneralSettingsPopoverTimeoutRegressionTests
{
    [Fact]
    public void General_settings_owns_a_vertical_scroll_viewport_like_other_long_management_pages()
    {
        var root = FindRepositoryRoot();
        var viewPath = Path.Combine(root, "src", "Butchi.App", "Settings", "GeneralSettingsView.cs");
        var source = File.ReadAllText(viewPath);

        Assert.Contains("Content = new ScrollViewer", source, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled", source, StringComparison.Ordinal);
    }

    [Fact]
    public void General_settings_exposes_a_dedicated_popover_auto_close_card()
    {
        var root = FindRepositoryRoot();
        var viewPath = Path.Combine(root, "src", "Butchi.App", "Settings", "GeneralSettingsView.cs");
        var source = File.ReadAllText(viewPath);

        Assert.Contains("SectionHeading(\"Popover\"", source, StringComparison.Ordinal);
        Assert.Contains("Auto-close after (seconds)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_session_uses_the_configured_popover_hide_delay()
    {
        var vm = new PopoverViewModel();
        var config = AppConfig.Default with { PopoverHideSeconds = 11 };

        vm.SetSession("hello", config);

        Assert.Equal(TimeSpan.FromSeconds(11), vm.AutoHideDelay);
    }

    [Fact]
    public void Popover_window_passes_the_configured_delay_to_both_inactivity_paths()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("HandleResultCompletedAsync(ViewModel.AutoHideDelay)", source, StringComparison.Ordinal);
        Assert.Contains("HandlePointerExitedAsync(ViewModel.AutoHideDelay)", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Butchi.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Butchi repository root.");
    }
}
