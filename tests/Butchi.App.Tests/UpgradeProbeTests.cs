using System.Reflection;
using Butchi.App.Diagnostics;
using Xunit;

namespace Butchi.App.Tests;

public sealed class UpgradeProbeTests
{
    [Fact]
    public void Probe_result_exposes_upgrade_compatibility_metadata_without_user_content()
    {
        var properties = typeof(ReleaseProbeResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ConfigReadable", properties);
        Assert.Contains("HistoryReadable", properties);
        Assert.Contains("HistoryEntryCount", properties);

        var result = ReleaseProbeResult.CreateSuccess("Butchi.Test", "0.1.0.1");
        Assert.Null(result.SelectedText);
        Assert.Null(result.PromptContent);
        Assert.Null(result.HistoryContent);
    }

    [Fact]
    public void Repository_defines_automated_N_to_N_plus_1_msix_upgrade_lifecycle()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "Release", "Test-MsixUpgrade.ps1");
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");

        Assert.True(File.Exists(scriptPath), $"Upgrade lifecycle script missing: {scriptPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("Test-MsixUpgrade.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("0.1.0.0", workflow, StringComparison.Ordinal);
        Assert.Contains("0.1.0.1", workflow, StringComparison.Ordinal);
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
