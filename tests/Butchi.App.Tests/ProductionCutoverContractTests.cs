using Xunit;

namespace Butchi.App.Tests;

public sealed class ProductionCutoverContractTests
{
    [Fact]
    public void Canonical_cutover_contract_forbids_mutable_butchi_fake_dependencies()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        var finalValidation = File.ReadAllText(Path.Combine(root, ".github", "workflows", "final-validation.yml"));

        Assert.DoesNotContain("successor implementation", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("butchi-fake/main", release, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("butchi-fake/main", finalValidation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("win-x64", release, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("win-arm64", release, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.msixbundle", finalValidation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.msixupload", finalValidation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cutover_procedure_documents_permanent_legacy_anchors()
    {
        var root = FindRepositoryRoot();
        var procedure = File.ReadAllText(Path.Combine(root, "docs", "production-cutover.md"));
        Assert.Contains("legacy-tauri", procedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pre-cutover", procedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", procedure, StringComparison.OrdinalIgnoreCase);
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

        throw new DirectoryNotFoundException("Could not locate Butchi repository root.");
    }
}
