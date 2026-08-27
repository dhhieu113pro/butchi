using Butchi.App.Screenshots;
using Butchi.Core.Configuration;
using Xunit;

namespace Butchi.App.Tests;

public sealed class UiParityContractTests
{
    [Fact]
    public void All_five_management_surfaces_are_real_and_non_placeholder()
    {
        var root = FindRepositoryRoot();
        var surfaces = new Dictionary<string, string[]>
        {
            [Path.Combine(root, "src", "Butchi.App", "Settings", "GeneralSettingsView.cs")] = ["Appearance", "Actions"],
            [Path.Combine(root, "src", "Butchi.App", "Settings", "PromptsView.cs")] = ["Translate", "Rewrite", "System prompt"],
            [Path.Combine(root, "src", "Butchi.App", "Models", "ModelManagementView.cs")] = ["Model setup", "Device", "Advanced inference settings"],
            [Path.Combine(root, "src", "Butchi.App", "History", "HistoryView.cs")] = ["History", "Search", "Retention"],
            [Path.Combine(root, "src", "Butchi.App", "About", "AboutPrivacyView.cs")] = ["About", "Privacy", "Delete local AI data"]
        };

        foreach (var (path, markers) in surfaces)
        {
            Assert.True(File.Exists(path), $"Missing Task 14 surface: {path}");
            var source = File.ReadAllText(path);
            foreach (var marker in markers)
                Assert.Contains(marker, source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Task 14.", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Prompts_inactive_mode_segment_inherits_theme_foreground()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src", "Butchi.App", "Settings", "PromptsView.cs");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("? ButchiTheme.WhiteBrush : null", source, StringComparison.Ordinal);
        Assert.Contains("if (_viewModel.Mode == PromptMode.Translate) _translateButton.Foreground = ButchiTheme.WhiteBrush;", source, StringComparison.Ordinal);
        Assert.Contains("if (_viewModel.Mode == PromptMode.Rewrite) _rewriteButton.Foreground = ButchiTheme.WhiteBrush;", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("light", AppThemePreference.Light)]
    [InlineData("dark", AppThemePreference.Dark)]
    [InlineData("system", AppThemePreference.System)]
    public void Screenshot_request_supports_deterministic_theme(string theme, AppThemePreference expected)
    {
        var parsed = ScreenshotRequest.TryParse(
            ["--screenshot", "out.png", "--theme", theme],
            out var request);

        Assert.True(parsed);
        Assert.NotNull(request);
        Assert.Equal(expected, request.Theme);
    }

    [Fact]
    public void Ci_uploads_complete_task14_evidence_and_validates_management_dimensions()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.Contains("name: butchi-task14-ui-evidence", workflow, StringComparison.Ordinal);
        Assert.Contains("settings-light.png", workflow, StringComparison.Ordinal);
        Assert.Contains("settings-dark.png", workflow, StringComparison.Ordinal);
        Assert.Contains("popover-light.png", workflow, StringComparison.Ordinal);
        Assert.Contains("popover-dark.png", workflow, StringComparison.Ordinal);
        Assert.Contains("--theme light", workflow, StringComparison.Ordinal);
        Assert.Contains("--theme dark", workflow, StringComparison.Ordinal);
        Assert.Contains("$image.Width -ne 1440", workflow, StringComparison.Ordinal);
        Assert.Contains("$image.Height -ne 900", workflow, StringComparison.Ordinal);
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
