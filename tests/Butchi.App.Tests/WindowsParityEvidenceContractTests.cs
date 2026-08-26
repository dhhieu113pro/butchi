using Xunit;

namespace Butchi.App.Tests;

public sealed class WindowsParityEvidenceContractTests
{
    [Fact]
    public void Task12_windows_parity_evidence_is_structured_validated_and_published()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "parity.yml");
        var validatorPath = Path.Combine(repoRoot, "scripts", "validate-parity-evidence.ps1");
        var templatePath = Path.Combine(repoRoot, "docs", "validation", "task12-parity-result.template.json");

        Assert.True(File.Exists(validatorPath), $"Missing Task 12 parity evidence validator: {validatorPath}");
        Assert.True(File.Exists(templatePath), $"Missing Task 12 parity evidence template: {templatePath}");

        var workflow = File.ReadAllText(workflowPath);
        var validator = File.ReadAllText(validatorPath);
        var template = File.ReadAllText(templatePath);

        Assert.Contains("validate-parity-evidence.ps1", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("task12-parity-result.json", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parity-summary.md", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actions/upload-artifact", workflow, StringComparison.OrdinalIgnoreCase);

        foreach (var architecture in new[] { "x64", "arm64" })
        {
            Assert.Contains(architecture, validator, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(architecture, template, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var check in new[]
        {
            "launch",
            "doubleCtrl",
            "selectionCapture",
            "clipboardPreserved",
            "translate",
            "rewrite",
            "cancel",
            "settings",
            "history",
            "models",
            "status",
            "modelLoading",
            "packagedLaunch"
        })
        {
            Assert.Contains(check, validator, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(check, template, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("ConvertFrom-Json", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parity-summary.md", validator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit 1", validator, StringComparison.OrdinalIgnoreCase);
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
