using Butchi.App.Diagnostics;
using Xunit;

namespace Butchi.App.Tests;

public sealed class InstalledAppProbeTests
{
    [Fact]
    public void TryParse_accepts_release_probe_with_output_path()
    {
        var parsed = ReleaseProbe.TryParse(["--release-probe", "probe.json"], out var outputPath);

        Assert.True(parsed);
        Assert.Equal("probe.json", outputPath);
    }

    [Fact]
    public void TryParse_rejects_missing_release_probe_output_path()
    {
        Assert.False(ReleaseProbe.TryParse(["--release-probe"], out _));
    }

    [Fact]
    public void Result_contains_only_privacy_safe_startup_markers()
    {
        var result = ReleaseProbeResult.CreateSuccess("Butchi.Test", "0.1.0.0");

        Assert.True(result.Success);
        Assert.True(result.CompositionHealthy);
        Assert.Equal("Butchi.Test", result.PackageIdentity);
        Assert.Equal("0.1.0.0", result.PackageVersion);
        Assert.Null(result.SelectedText);
        Assert.Null(result.PromptContent);
        Assert.Null(result.HistoryContent);
    }

    [Fact]
    public void Installed_msix_probe_is_bounded_and_release_runs_cancel_stale_pr_verification()
    {
        var repoRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Release", "Test-InstalledMsix.ps1"));
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("WaitForExit(30000)", script, StringComparison.Ordinal);
        Assert.Contains("Kill", script, StringComparison.Ordinal);
        Assert.Contains("probeProduced", script, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: true", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installed_msix_release_job_is_time_bounded_and_emits_stage_markers()
    {
        var repoRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Release", "Test-InstalledMsix.ps1"));
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("timeout-minutes: 5", workflow, StringComparison.OrdinalIgnoreCase);
        foreach (var marker in new[] { "INSTALL_BEGIN", "INSTALL_END", "PROBE_LAUNCH", "PROBE_EXIT", "UNINSTALL_BEGIN", "UNINSTALL_END" })
            Assert.Contains(marker, script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installed_msix_smoke_uses_machine_root_trust_on_disposable_runner()
    {
        var repoRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Release", "Test-InstalledMsix.ps1"));
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("Cert:\\CurrentUser\\TrustedPeople", workflow, StringComparison.Ordinal);
        Assert.Contains("System.Security.Cryptography.X509Certificates.X509Store", workflow, StringComparison.Ordinal);
        Assert.Contains("StoreName]::Root", workflow, StringComparison.Ordinal);
        Assert.Contains("StoreLocation]::LocalMachine", workflow, StringComparison.Ordinal);
        Assert.Contains(".Add($rootCertificate)", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreLocation]::CurrentUser", workflow, StringComparison.Ordinal);
        Assert.Contains("Cert:\\LocalMachine\\Root\\$env:CI_SIGNING_THUMBPRINT", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Cert:\\CurrentUser\\Root", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Cert:\\CurrentUser\\Root", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installed_msix_probe_activates_the_registered_application_instead_of_launching_the_exe_directly()
    {
        var repoRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Release", "Test-InstalledMsix.ps1"));

        Assert.Contains("IApplicationActivationManager", script, StringComparison.Ordinal);
        Assert.Contains("ActivateApplication", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-Process -FilePath $exe", script, StringComparison.Ordinal);
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
