using System.Xml.Linq;
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
    public void Store_manifest_declares_disabled_Butchi_startup_task()
    {
        var repoRoot = FindRepositoryRoot();
        var path = Path.Combine(repoRoot, "store", "Package.appxmanifest.template");
        var document = XDocument.Load(path);
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace uap5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";

        var package = document.Root!;
        Assert.Contains("uap5", package.Attribute("IgnorableNamespaces")?.Value ?? string.Empty, StringComparison.Ordinal);

        var extension = document
            .Descendants(uap5 + "Extension")
            .SingleOrDefault(x => (string?)x.Attribute("Category") == "windows.startupTask");
        Assert.NotNull(extension);
        Assert.Equal("butchi.exe", (string?)extension.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)extension.Attribute("EntryPoint"));

        var task = extension.Element(uap5 + "StartupTask");
        Assert.NotNull(task);
        Assert.Equal("ButchiStartup", (string?)task.Attribute("TaskId"));
        Assert.Equal("false", (string?)task.Attribute("Enabled"));
        Assert.Equal("Butchi", (string?)task.Attribute("DisplayName"));

        Assert.Single(document.Descendants(foundation + "Application"));
    }

    [Fact]
    public void Store_validator_enforces_startup_task_in_staged_and_packaged_manifests()
    {
        var validator = ReadValidator();

        Assert.Contains("Assert-StartupTask", validator, StringComparison.Ordinal);
        Assert.Contains("windows.startupTask", validator, StringComparison.Ordinal);
        Assert.Contains("ButchiStartup", validator, StringComparison.Ordinal);
        Assert.Contains("Windows.FullTrustApplication", validator, StringComparison.Ordinal);
        Assert.Contains("Enabled", validator, StringComparison.Ordinal);
        Assert.Contains("DisplayName", validator, StringComparison.Ordinal);
        Assert.Contains("Assert-StartupTask -Manifest $manifest -Context \"Staged manifest\"", validator, StringComparison.Ordinal);
        Assert.Contains("Assert-StartupTask -Manifest $packageManifest -Context \"Packaged manifest\"", validator, StringComparison.Ordinal);
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
