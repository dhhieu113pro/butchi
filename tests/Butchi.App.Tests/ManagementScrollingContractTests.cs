using Xunit;

namespace Butchi.App.Tests;

public sealed class ManagementScrollingContractTests
{
    [Fact]
    public void Management_shell_owns_the_vertical_scroll_viewport()
    {
        var root = FindRepositoryRoot();
        var shellPath = Path.Combine(root, "src", "Butchi.App", "Management", "ManagementWindow.cs");
        var shell = File.ReadAllText(shellPath);

        Assert.Contains("ScrollViewer _contentScroll", shell, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility", shell, StringComparison.Ordinal);
        Assert.Contains("_contentScroll.Content = BuildPage(page)", shell, StringComparison.Ordinal);
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
