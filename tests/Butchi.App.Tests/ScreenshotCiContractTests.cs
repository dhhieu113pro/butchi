using Xunit;

namespace Butchi.App.Tests;

public sealed class ScreenshotCiContractTests
{
    [Fact]
    public void Ci_captures_default_and_management_views_at_desktop_size()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");

        Assert.True(File.Exists(workflowPath), $"Missing CI workflow: {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("$pages = @('default', 'settings', 'history', 'models', 'status')", workflow, StringComparison.Ordinal);
        Assert.Contains("--width 1440 --height 900", workflow, StringComparison.Ordinal);
        Assert.Contains("name: butchi-ui-screenshots", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/ui/*.png", workflow, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Butchi.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Butchi repository root from the test output directory.");
    }
}
