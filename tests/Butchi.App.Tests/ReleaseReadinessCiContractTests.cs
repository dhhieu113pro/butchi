using Xunit;

namespace Butchi.App.Tests;

public sealed class ReleaseReadinessCiContractTests
{
    [Fact]
    public void Repository_defines_a_single_release_readiness_workflow()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "release-readiness.yml");

        Assert.True(File.Exists(workflowPath), $"Missing final release-readiness workflow: {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("name: Release Readiness", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pull_request", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workflow_dispatch", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cancel-in-progress: true", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("continue-on-error", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_readiness_gate_covers_build_visual_publish_package_and_runtime_evidence()
    {
        var workflow = ReadReadinessWorkflow();

        foreach (var expected in new[]
        {
            "dotnet build Butchi.slnx",
            "dotnet test Butchi.slnx",
            "Capture deterministic Avalonia UI evidence",
            "win-x64",
            "win-arm64",
            "Validate-StorePackage.ps1",
            "New-CiMsixSigningCertificate.ps1",
            "Sign-CiMsix.ps1",
            "Test-InstalledMsix.ps1",
            "Test-MsixUpgrade.ps1",
            "ReleaseProbe",
            ".msixbundle",
            ".msixupload"
        })
        {
            Assert.Contains(expected, workflow, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Release_readiness_artifact_inventory_rejects_private_signing_material_and_requires_public_evidence()
    {
        var workflow = ReadReadinessWorkflow();

        Assert.Contains("artifact inventory", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".pfx", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private key", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("butchi-store-upload", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ui-screenshots", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotSigned", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("artifacts/ci-install/**", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artifacts/ci-signing/**", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Main_ci_and_release_workflows_reference_the_final_readiness_gate()
    {
        var repoRoot = FindRepositoryRoot();
        var ci = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "ci.yml"));
        var release = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("release-readiness", ci, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("release-readiness", release, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadReadinessWorkflow()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "release-readiness.yml");
        Assert.True(File.Exists(workflowPath), $"Missing final release-readiness workflow: {workflowPath}");
        return File.ReadAllText(workflowPath);
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
