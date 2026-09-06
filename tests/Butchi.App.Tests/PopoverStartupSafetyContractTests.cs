using Xunit;

namespace Butchi.App.Tests;

public sealed class PopoverStartupSafetyContractTests
{
    [Fact]
    public void Popover_host_does_not_use_transitioning_content_control_during_runtime_startup()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.DoesNotContain("TransitioningContentControl", source, StringComparison.Ordinal);
        Assert.Contains("private readonly ContentControl _islandHost = new();", source, StringComparison.Ordinal);
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
