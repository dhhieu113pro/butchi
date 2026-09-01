using Xunit;

namespace Butchi.App.Tests;

public sealed class E2eCiContractTests
{
    [Fact]
    public void Repository_wires_desktop_e2e_project_into_ci()
    {
        var root = FindRepositoryRoot();

        var solution = File.ReadAllText(Path.Combine(root, "Butchi.slnx"));
        Assert.Contains("tests/Butchi.E2E.Tests/Butchi.E2E.Tests.csproj", solution, StringComparison.Ordinal);

        var projectPath = Path.Combine(root, "tests", "Butchi.E2E.Tests", "Butchi.E2E.Tests.csproj");
        Assert.True(File.Exists(projectPath), $"E2E project missing: {projectPath}");

        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        Assert.Contains("e2e:", workflow, StringComparison.Ordinal);
        Assert.Contains("Butchi.E2E.Tests", workflow, StringComparison.Ordinal);
        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not locate Butchi.slnx from the test output directory.");
    }
}
