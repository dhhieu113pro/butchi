using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Butchi.App.Management;
using Xunit;

namespace Butchi.App.Tests;

public sealed class ManagementScrollingContractTests
{
    [Fact]
    public void Management_content_scroll_configures_axes_and_resets_when_content_changes()
    {
        var first = new Border();
        var second = new Border();
        var scroll = new ManagementContentScroll(first);

        Assert.Same(first, scroll.Content);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);

        scroll.Offset = new Vector(0, 120);
        scroll.Show(second);

        Assert.Same(second, scroll.Content);
        Assert.Equal(Vector.Zero, scroll.Offset);
    }

    [Fact]
    public void Management_shell_owns_the_vertical_scroll_viewport()
    {
        var root = FindRepositoryRoot();
        var shellPath = Path.Combine(root, "src", "Butchi.App", "Management", "ManagementWindow.cs");
        var shell = File.ReadAllText(shellPath);

        Assert.Contains("ManagementContentScroll _contentScroll", shell, StringComparison.Ordinal);
        Assert.Contains("_contentScroll.Show(BuildPage(page))", shell, StringComparison.Ordinal);
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
