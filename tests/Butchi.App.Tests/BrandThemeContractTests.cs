using System.Reflection;
using Avalonia.Media;
using Butchi.App.Styling;
using Xunit;

namespace Butchi.App.Tests;

public sealed class BrandThemeContractTests
{
    [Fact]
    public void Brand_palette_matches_the_logo_colors()
    {
        Assert.Equal(Color.Parse("#24C8DB"), ButchiTheme.BrandCyan);
        Assert.Equal(Color.Parse("#FFC131"), ButchiTheme.BrandYellow);
        Assert.Equal(Color.Parse("#365CF5"), ButchiTheme.Cobalt);
    }

    [Fact]
    public void Theme_exposes_adaptive_shell_navigation_card_and_local_status_tokens()
    {
        var names = typeof(ButchiTheme)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("NavigationSurfaceBrush", names);
        Assert.Contains("NavigationForegroundBrush", names);
        Assert.Contains("SelectedNavigationSurfaceBrush", names);
        Assert.Contains("SelectedNavigationForegroundBrush", names);
        Assert.Contains("SelectedNavigationIndicatorBrush", names);
        Assert.Contains("CardSurfaceBrush", names);
        Assert.Contains("LocalStatusSurfaceBrush", names);
        Assert.Contains("LocalStatusForegroundBrush", names);
    }

    [Fact]
    public void Management_and_popover_refresh_brand_surfaces_when_theme_changes()
    {
        var repoRoot = FindRepositoryRoot();
        var management = File.ReadAllText(Path.Combine(repoRoot, "src", "Butchi.App", "Management", "ManagementWindow.cs"));
        var popover = File.ReadAllText(Path.Combine(repoRoot, "src", "Butchi.App", "Popover", "PopoverWindow.cs"));

        Assert.Contains("ActualThemeVariantChanged", management, StringComparison.Ordinal);
        Assert.Contains("NavigationSurfaceBrush", management, StringComparison.Ordinal);
        Assert.Contains("SelectedNavigationIndicatorBrush", management, StringComparison.Ordinal);
        Assert.DoesNotContain("Background = ButchiTheme.CobaltDarkBrush", management, StringComparison.Ordinal);

        Assert.Contains("ActualThemeVariantChanged", popover, StringComparison.Ordinal);
        Assert.Contains("LocalStatusSurfaceBrush", popover, StringComparison.Ordinal);
        Assert.Contains("CardSurfaceBrush", popover, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not locate Butchi repository root from the test output directory.");
    }
}
