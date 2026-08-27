using Xunit;

namespace Butchi.App.Tests;

public sealed class CiMsixSigningContractTests
{
    [Fact]
    public void Repository_defines_ephemeral_certificate_and_signing_scripts()
    {
        var repoRoot = FindRepositoryRoot();
        var certPath = Path.Combine(repoRoot, "scripts", "Release", "New-CiMsixSigningCertificate.ps1");
        var signPath = Path.Combine(repoRoot, "scripts", "Release", "Sign-CiMsix.ps1");

        Assert.True(File.Exists(certPath), $"Missing CI certificate helper: {certPath}");
        Assert.True(File.Exists(signPath), $"Missing CI MSIX signing helper: {signPath}");
    }

    [Fact]
    public void Signing_helper_rejects_production_artifact_root_and_verifies_signature()
    {
        var sign = ReadScript("Sign-CiMsix.ps1");

        Assert.Contains("ProductionRoot", sign, StringComparison.Ordinal);
        Assert.Contains("Resolve-Path", sign, StringComparison.Ordinal);
        Assert.Contains("SignTool", sign, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-AuthenticodeSignature", sign, StringComparison.Ordinal);
        Assert.Contains("SignerCertificate", sign, StringComparison.Ordinal);
        Assert.Contains("Thumbprint", sign, StringComparison.Ordinal);
    }

    [Fact]
    public void Signing_helper_uses_signtool_to_verify_the_signed_msix_package()
    {
        var sign = ReadScript("Sign-CiMsix.ps1");

        Assert.Contains("verify", sign, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/pa", sign, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Certificate_helper_creates_short_lived_codesigning_cert_and_public_export_only()
    {
        var cert = ReadScript("New-CiMsixSigningCertificate.ps1");

        Assert.Contains("New-SelfSignedCertificate", cert, StringComparison.Ordinal);
        Assert.Contains("CodeSigningCert", cert, StringComparison.Ordinal);
        Assert.Contains("Export-Certificate", cert, StringComparison.Ordinal);
        Assert.DoesNotContain("Export-PfxCertificate", cert, StringComparison.Ordinal);
        Assert.Contains("Remove-Item", cert, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_workflow_signs_only_ci_copy_and_never_uploads_private_key_material()
    {
        var repoRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("artifacts/ci-install", workflow, StringComparison.Ordinal);
        Assert.Contains("New-CiMsixSigningCertificate.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("Sign-CiMsix.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("*.pfx", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Export-PfxCertificate", workflow, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadScript(string name)
    {
        var repoRoot = FindRepositoryRoot();
        var path = Path.Combine(repoRoot, "scripts", "Release", name);
        Assert.True(File.Exists(path), $"Missing release helper: {path}");
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
