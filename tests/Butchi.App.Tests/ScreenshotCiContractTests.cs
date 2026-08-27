using Xunit;

namespace Butchi.App.Tests;

public sealed class ScreenshotCiContractTests
{
    [Fact]
    public void Ci_captures_real_popover_and_management_views()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
        var appPath = Path.Combine(repoRoot, "src", "Butchi.App", "Startup", "ScreenshotStartup.cs");
        var runnerPath = Path.Combine(repoRoot, "src", "Butchi.App", "Screenshots", "ScreenshotRunner.cs");

        Assert.True(File.Exists(workflowPath), $"Missing CI workflow: {workflowPath}");
        Assert.True(File.Exists(appPath), $"Missing screenshot startup: {appPath}");
        Assert.True(File.Exists(runnerPath), $"Missing screenshot runner: {runnerPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("--screenshot-popover \"artifacts/ui/popover-light.png\" --theme light", workflow, StringComparison.Ordinal);
        Assert.Contains("--screenshot-popover \"artifacts/ui/popover-dark.png\" --theme dark", workflow, StringComparison.Ordinal);
        Assert.Contains("$pages = @('prompts', 'history', 'models', 'status')", workflow, StringComparison.Ordinal);
        Assert.Contains("--width 1440 --height 900 --theme light", workflow, StringComparison.Ordinal);
        Assert.Contains("name: butchi-task14-ui-evidence", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/ui/*.png", workflow, StringComparison.Ordinal);

        var startup = File.ReadAllText(appPath);
        Assert.Contains("--screenshot-popover", startup, StringComparison.Ordinal);
        Assert.Contains("ScreenshotRequest.ParseTheme", startup, StringComparison.Ordinal);

        var runner = File.ReadAllText(runnerPath);
        Assert.Contains("RunPopover", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void Management_capture_renders_content_offscreen_at_requested_size()
    {
        var repoRoot = FindRepositoryRoot();
        var runnerPath = Path.Combine(repoRoot, "src", "Butchi.App", "Screenshots", "ScreenshotRunner.cs");

        Assert.True(File.Exists(runnerPath), $"Missing screenshot runner: {runnerPath}");

        var runner = File.ReadAllText(runnerPath);
        Assert.Contains("RenderManagementContent", runner, StringComparison.Ordinal);
        Assert.Contains("window.Content is not Control content", runner, StringComparison.Ordinal);
        Assert.Contains("panel.Background ??= window.Background", runner, StringComparison.Ordinal);
        Assert.Contains("content.Measure(new Size(width, height))", runner, StringComparison.Ordinal);
        Assert.Contains("content.Arrange(new Rect(0, 0, width, height))", runner, StringComparison.Ordinal);
        Assert.Contains("bitmap.Render(content)", runner, StringComparison.Ordinal);
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
