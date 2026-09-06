using Xunit;

namespace Butchi.App.Tests;

public sealed class ManagementScrollingContractTests
{
    [Fact]
    public void Management_shell_does_not_wrap_page_owned_scrollers_in_another_vertical_scroll_viewport()
    {
        var root = FindRepositoryRoot();
        var shellPath = Path.Combine(root, "src", "Butchi.App", "Management", "ManagementWindow.cs");
        var shell = File.ReadAllText(shellPath);

        Assert.DoesNotContain("ManagementContentScroll", shell, StringComparison.Ordinal);
        Assert.Contains("_contentHost.Child = BuildPage(page)", shell, StringComparison.Ordinal);
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
