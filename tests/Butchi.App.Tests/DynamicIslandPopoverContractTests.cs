using Xunit;

namespace Butchi.App.Tests;

public sealed class DynamicIslandPopoverContractTests
{
    [Fact]
    public void Popover_window_switches_between_compact_island_and_expanded_content()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("Width = 420", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 0", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsCompact", source, StringComparison.Ordinal);
        Assert.Contains("BuildCompactIsland", source, StringComparison.Ordinal);
        Assert.Contains("BuildExpandedIsland", source, StringComparison.Ordinal);
        Assert.Contains("Translating…", source, StringComparison.Ordinal);
        Assert.Contains("Rewriting…", source, StringComparison.Ordinal);
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
