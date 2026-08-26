using Xunit;

namespace Butchi.App.Tests;

public sealed class FinalMigrationGateContractTests
{
    [Fact]
    public void Task12_final_gate_requires_parity_performance_and_release_packaging_evidence()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "final-validation.yml");
        var verifierPath = Path.Combine(repoRoot, "scripts", "verify-final-migration.ps1");

        Assert.True(File.Exists(workflowPath), $"Missing final migration workflow: {workflowPath}");
        Assert.True(File.Exists(verifierPath), $"Missing final migration verifier: {verifierPath}");

        var workflow = File.ReadAllText(workflowPath);
        var verifier = File.ReadAllText(verifierPath);

        foreach (var evidence in new[]
        {
            "performance-summary.json",
            "task12-parity-result.json",
            "*.msix",
            "*.msixbundle",
            "*.msixupload",
            "migration-summary.md"
        })
        {
            Assert.Contains(evidence, workflow, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(evidence, verifier, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var architecture in new[] { "x64", "arm64" })
        {
            Assert.Contains(architecture, verifier, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("passed", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit 1", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actions/upload-artifact", workflow, StringComparison.OrdinalIgnoreCase);
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
