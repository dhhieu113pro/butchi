using Xunit;

namespace Butchi.App.Tests;

public sealed class StorePackageContractTests
{
    [Fact]
    public void Repository_defines_store_package_validator()
    {
        var repoRoot = FindRepositoryRoot();
        var validatorPath = Path.Combine(repoRoot, "scripts", "Release", "Validate-StorePackage.ps1");

        Assert.True(File.Exists(validatorPath), $"Missing Store package validator: {validatorPath}");

        var validator = File.ReadAllText(validatorPath);
        Assert.Contains("StagePath", validator, StringComparison.Ordinal);
        Assert.Contains("Architecture", validator, StringComparison.Ordinal);
        Assert.Contains("Version", validator, StringComparison.Ordinal);
        Assert.Contains("BundlePath", validator, StringComparison.Ordinal);
        Assert.Contains("UploadPath", validator, StringComparison.Ordinal);
    }

    [Fact]
    public void Store_package_validator_enforces_payload_and_manifest_contracts()
    {
        var validator = ReadValidator();

        Assert.Contains("Package.appxmanifest", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows.FullTrustApplication", validator, StringComparison.Ordinal);
        Assert.Contains("butchi.exe", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Square150x150Logo.png", validator, StringComparison.Ordinal);
        Assert.Contains("Square44x44Logo.png", validator, StringComparison.Ordinal);
        Assert.Contains("StoreLogo.png", validator, StringComparison.Ordinal);
        Assert.Contains("x64", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arm64", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("four", validator, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_workflow_runs_validator_for_architecture_packages_and_bundle_upload()
    {
        var repoRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("Validate-StorePackage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("-StagePath", workflow, StringComparison.Ordinal);
        Assert.Contains("-Architecture", workflow, StringComparison.Ordinal);
        Assert.Contains("-Version", workflow, StringComparison.Ordinal);
        Assert.Contains("-BundlePath", workflow, StringComparison.Ordinal);
        Assert.Contains("-UploadPath", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_store_artifact_validation_rejects_signed_outputs()
    {
        var validator = ReadValidator();

        Assert.Contains("unsigned", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-AuthenticodeSignature", validator, StringComparison.Ordinal);
        Assert.Contains("NotSigned", validator, StringComparison.Ordinal);
    }

    private static string ReadValidator()
    {
        var repoRoot = FindRepositoryRoot();
        var validatorPath = Path.Combine(repoRoot, "scripts", "Release", "Validate-StorePackage.ps1");
        Assert.True(File.Exists(validatorPath), $"Missing Store package validator: {validatorPath}");
        return File.ReadAllText(validatorPath);
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
