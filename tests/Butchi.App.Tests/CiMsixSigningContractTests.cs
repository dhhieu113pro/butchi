using Xunit;

namespace Butchi.App.Tests;

public sealed class CiMsixSigningContractTests
{
    [Fact]
    public void Repository_defines_ephemeral_certificate_and_signing_helpers()
    {
        var repoRoot = FindRepositoryRoot();
        var certificatePath = Path.Combine(repoRoot, "scripts", "Release", "New-CiMsixSigningCertificate.ps1");
        var signingPath = Path.Combine(repoRoot, "scripts", "Release", "Sign-CiMsix.ps1");

        Assert.True(File.Exists(certificatePath), $"Missing CI signing certificate helper: {certificatePath}");
        Assert.True(File.Exists(signingPath), $"Missing CI MSIX signing helper: {signingPath}");
    }

    [Fact]
    public void Certificate_helper_creates_short_lived_code_signing_certificate_without_exporting_private_key()
    {
        var script = ReadScript("New-CiMsixSigningCertificate.ps1");

        Assert.Contains("New-SelfSignedCertificate", script, StringComparison.Ordinal);
        Assert.Contains("CodeSigningCert", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Export-Certificate", script, StringComparison.Ordinal);
        Assert.Contains("NotAfter", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Export-PfxCertificate", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".pfx", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Signing_helper_rejects_production_paths_and_verifies_the_expected_signer()
    {
        var script = ReadScript("Sign-CiMsix.ps1");

        Assert.Contains("ProductionRoot", script, StringComparison.Ordinal);
        Assert.Contains("CertificateThumbprint", script, StringComparison.Ordinal);
        Assert.Contains("signtool", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-AuthenticodeSignature", script, StringComparison.Ordinal);
        Assert.Contains("SignerCertificate", script, StringComparison.Ordinal);
        Assert.Contains("Thumbprint", script, StringComparison.Ordinal);
        Assert.Contains("throw", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_workflow_signs_only_a_copied_x64_ci_package_and_always_cleans_certificate_material()
    {
        var repoRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("artifacts/ci-install", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("New-CiMsixSigningCertificate.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("Sign-CiMsix.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Remove-Item", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*.pfx", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artifacts/ci-install/*.msix", workflow, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadScript(string name)
    {
        var repoRoot = FindRepositoryRoot();
        var path = Path.Combine(repoRoot, "scripts", "Release", name);
        Assert.True(File.Exists(path), $"Missing Task 15.2 script: {path}");
        return File.ReadAllText(path);
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
