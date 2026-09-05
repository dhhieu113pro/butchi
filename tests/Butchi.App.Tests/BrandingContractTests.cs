using Xunit;

namespace Butchi.App.Tests;

public sealed class BrandingContractTests
{
    [Fact]
    public void Avalonia_app_uses_one_current_butchi_brand_everywhere()
    {
        var repoRoot = FindRepositoryRoot();
        var sourceArtworkPath = Path.Combine(repoRoot, "assets", "butchi-logo.svg");
        var appIconPath = Path.Combine(repoRoot, "src", "Butchi.App", "Assets", "Butchi.ico");
        var appLogoPath = Path.Combine(repoRoot, "src", "Butchi.App", "Assets", "ButchiLogo.png");
        var storeLogoPath = Path.Combine(repoRoot, "store", "Assets", "StoreLogo.png");
        var square44Path = Path.Combine(repoRoot, "store", "Assets", "Square44x44Logo.png");
        var square150Path = Path.Combine(repoRoot, "store", "Assets", "Square150x150Logo.png");

        Assert.True(File.Exists(sourceArtworkPath), $"Missing canonical Butchi source artwork: {sourceArtworkPath}");
        Assert.True(File.Exists(appIconPath), $"Missing committed Windows application icon: {appIconPath}");
        Assert.True(File.Exists(appLogoPath), $"Missing Avalonia logo asset: {appLogoPath}");
        Assert.True(File.Exists(storeLogoPath), $"Missing Store logo asset: {storeLogoPath}");
        Assert.True(File.Exists(square44Path), $"Missing Store square 44 logo: {square44Path}");
        Assert.True(File.Exists(square150Path), $"Missing Store square 150 logo: {square150Path}");

        var project = File.ReadAllText(Path.Combine(repoRoot, "src", "Butchi.App", "Butchi.App.csproj"));
        Assert.Contains("Assets\\ButchiLogo.png", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>Assets\\Butchi.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateWindowsApplicationIcon", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageToIco.Dnx", project, StringComparison.Ordinal);

        var brandAssets = File.ReadAllText(Path.Combine(repoRoot, "src", "Butchi.App", "Branding", "BrandAssets.cs"));
        Assert.Contains("avares://Butchi.App/Assets/ButchiLogo.png", brandAssets, StringComparison.Ordinal);
        Assert.Contains("avares://Butchi.App/!__AvaloniaDefaultWindowIcon", brandAssets, StringComparison.Ordinal);

        var runtime = File.ReadAllText(Path.Combine(repoRoot, "src", "Butchi.App", "Startup", "ButchiRuntime.cs"));
        Assert.Contains("BrandAssets.CreateWindowIcon()", runtime, StringComparison.Ordinal);

        var management = File.ReadAllText(Path.Combine(repoRoot, "src", "Butchi.App", "Management", "ManagementWindow.cs"));
        Assert.Contains("Icon = BrandAssets.CreateWindowIcon()", management, StringComparison.Ordinal);

        var popover = File.ReadAllText(Path.Combine(repoRoot, "src", "Butchi.App", "Popover", "PopoverWindow.cs"));
        Assert.Contains("Icon = BrandAssets.CreateWindowIcon()", popover, StringComparison.Ordinal);

        var release = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));
        Assert.Contains("Copy-Item \"store/Assets/*\" \"$stage/Assets\"", release, StringComparison.Ordinal);
        Assert.DoesNotContain("raw.githubusercontent.com/dhhieu113pro/butchi/$baseline/src-tauri/icons", release, StringComparison.Ordinal);

        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        Assert.Contains("assets/butchi-logo.svg", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("src/Butchi.App/Assets/ButchiLogo.png", readme, StringComparison.Ordinal);
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
