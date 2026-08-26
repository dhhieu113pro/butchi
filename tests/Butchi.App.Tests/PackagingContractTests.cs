using Xunit;

namespace Butchi.App.Tests;

public sealed class PackagingContractTests
{
    [Fact]
    public void Store_manifest_template_preserves_required_identity_placeholders()
    {
        var repoRoot = FindRepositoryRoot();
        var manifestPath = Path.Combine(repoRoot, "store", "Package.appxmanifest.template");

        Assert.True(File.Exists(manifestPath), $"Missing Store manifest template: {manifestPath}");

        var manifest = File.ReadAllText(manifestPath);
        Assert.Contains("__PACKAGE_NAME__", manifest, StringComparison.Ordinal);
        Assert.Contains("__PUBLISHER__", manifest, StringComparison.Ordinal);
        Assert.Contains("__PUBLISHER_DISPLAY_NAME__", manifest, StringComparison.Ordinal);
        Assert.Contains("__VERSION__", manifest, StringComparison.Ordinal);
        Assert.Contains("__ARCHITECTURE__", manifest, StringComparison.Ordinal);
        Assert.Contains("ProcessorArchitecture=\"__ARCHITECTURE__\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Butchi", manifest, StringComparison.Ordinal);
        Assert.Contains("butchi.exe", manifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_workflow_builds_both_store_architectures_and_real_store_upload()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");

        Assert.True(File.Exists(workflowPath), $"Missing release workflow: {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("tags:", workflow, StringComparison.Ordinal);
        Assert.Contains("v*", workflow, StringComparison.Ordinal);
        Assert.Contains("win-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("win-arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("__ARCHITECTURE__", workflow, StringComparison.Ordinal);
        Assert.Contains("matrix.arch", workflow, StringComparison.Ordinal);
        Assert.Contains("setup-WinAppCli", workflow, StringComparison.Ordinal);
        Assert.Contains("winapp pack", workflow, StringComparison.Ordinal);
        Assert.Contains("MakeAppx.exe", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bundle", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".msixbundle", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".msixupload", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("softprops/action-gh-release", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_workflow_reserves_store_revision_component()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");

        Assert.True(File.Exists(workflowPath), $"Missing release workflow: {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("STORE_VERSION_REVISION=0", workflow, StringComparison.Ordinal);
        Assert.Contains("MSIX_VERSION=$major.$minor.$build.$env:STORE_VERSION_REVISION", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_workflow_reads_public_store_identity_from_repository_variables()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");

        Assert.True(File.Exists(workflowPath), $"Missing release workflow: {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("STORE_PACKAGE_IDENTITY_NAME: ${{ vars.STORE_PACKAGE_IDENTITY_NAME }}", workflow, StringComparison.Ordinal);
        Assert.Contains("STORE_PACKAGE_PUBLISHER: ${{ vars.STORE_PACKAGE_PUBLISHER }}", workflow, StringComparison.Ordinal);
        Assert.Contains("STORE_PUBLISHER_DISPLAY_NAME: ${{ vars.STORE_PUBLISHER_DISPLAY_NAME }}", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.STORE_PACKAGE_IDENTITY_NAME", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.STORE_PACKAGE_PUBLISHER", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.STORE_PUBLISHER_DISPLAY_NAME", workflow, StringComparison.Ordinal);
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
