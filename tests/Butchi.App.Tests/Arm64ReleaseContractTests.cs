using Xunit;

namespace Butchi.App.Tests;

public sealed class Arm64ReleaseContractTests
{
    [Fact]
    public void Store_validator_inspects_architecture_specific_msix_payload_contents()
    {
        var validator = ReadValidator();

        Assert.Contains("OpenRead((Resolve-Path $PackagePath))", validator, StringComparison.Ordinal);
        Assert.Contains("AppxManifest.xml", validator, StringComparison.Ordinal);
        Assert.Contains("coreclr.dll", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hostfxr.dll", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hostpolicy.dll", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("butchi.exe", validator, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bundle_validation_requires_x64_and_arm64_packages_with_matching_identity_and_version()
    {
        var validator = ReadValidator();

        Assert.Contains("ProcessorArchitecture", validator, StringComparison.Ordinal);
        Assert.Contains("x64", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arm64", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bundleIdentities", validator, StringComparison.Ordinal);
        Assert.Contains("bundleVersions", validator, StringComparison.Ordinal);
        Assert.Contains("bundleArchitectures", validator, StringComparison.Ordinal);
    }

    [Fact]
    public void Arm64_validation_is_structural_and_native_runtime_smoke_remains_x64_only()
    {
        var repoRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("win-arm64", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Validate-StorePackage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("butchi-msix-x64", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Butchi_CI_x64.msix", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Butchi_CI_arm64.msix", workflow, StringComparison.OrdinalIgnoreCase);
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
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Butchi repository root from the test output directory.");
    }
}
