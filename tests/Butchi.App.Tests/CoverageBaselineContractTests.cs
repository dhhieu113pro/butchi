using Xunit;

namespace Butchi.App.Tests;

public sealed class CoverageBaselineContractTests
{
    [Fact]
    public void CI_collects_and_publishes_solution_coverage()
    {
        var repoRoot = FindRepositoryRoot();
        var packagesPath = Path.Combine(repoRoot, "Directory.Packages.props");
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");

        var packages = File.ReadAllText(packagesPath);
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("coverlet.collector", packages, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("XPlat Code Coverage", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("coverage.cobertura.xml", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actions/upload-artifact", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("coverage", workflow, StringComparison.OrdinalIgnoreCase);
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
