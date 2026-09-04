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

        Assert.Contains("CompactWidth = 420", source, StringComparison.Ordinal);
        Assert.Contains("ExpandedWidth = 760", source, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsCompact", source, StringComparison.Ordinal);
        Assert.Contains("BuildCompactIsland", source, StringComparison.Ordinal);
        Assert.Contains("BuildExpandedIsland", source, StringComparison.Ordinal);
        Assert.Contains("Translating…", source, StringComparison.Ordinal);
        Assert.Contains("Rewriting…", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Expanded_popover_matches_the_approved_wide_result_first_design()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("BuildCenteredLogo", source, StringComparison.Ordinal);
        Assert.Contains("ModeIconButton", source, StringComparison.Ordinal);
        Assert.Contains("TextTrimming.CharacterEllipsis", source, StringComparison.Ordinal);
        Assert.Contains("BuildThinkingDisclosure", source, StringComparison.Ordinal);
        Assert.Contains("BuildResultPanel", source, StringComparison.Ordinal);
        Assert.Contains("ResultScrollMaxHeight = 340", source, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"Local AI\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"On device\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Popover_animates_only_between_compact_and_expanded_states()
    {
        var root = FindRepositoryRoot();
        var windowPath = Path.Combine(root, "src", "Butchi.App", "Popover", "PopoverWindow.cs");
        var source = File.ReadAllText(windowPath);

        Assert.Contains("TransitioningContentControl", source, StringComparison.Ordinal);
        Assert.Contains("CrossFade", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", source, StringComparison.Ordinal);
        Assert.Contains("compact != _lastCompactState", source, StringComparison.Ordinal);
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
