using Xunit;

namespace Butchi.App.Tests;

public sealed class Task14PopoverUiContractTests
{
    [Fact]
    public void Inactive_action_segment_inherits_theme_foreground_instead_of_local_null()
    {
        var root = FindRepositoryRoot();
        var popoverPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var popover = File.ReadAllText(popoverPath);

        Assert.DoesNotContain("Foreground = selected ? ButchiTheme.WhiteBrush : null", popover, StringComparison.Ordinal);
        Assert.Contains("if (selected) button.Foreground = ButchiTheme.WhiteBrush;", popover, StringComparison.Ordinal);
    }

    [Fact]
    public void Popover_wires_pointer_hover_close_handlers()
    {
        var root = FindRepositoryRoot();
        var popoverPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var popover = File.ReadAllText(popoverPath);

        Assert.Contains("PointerEntered += OnPointerEntered;", popover, StringComparison.Ordinal);
        Assert.Contains("PointerExited += OnPointerExited;", popover, StringComparison.Ordinal);
        Assert.Contains("_controller.HandlePointerEntered();", popover, StringComparison.Ordinal);
        Assert.Contains("await _controller.HandlePointerExitedAsync(ViewModel.AutoHideDelay)", popover, StringComparison.Ordinal);
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
