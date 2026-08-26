using Xunit;

namespace Butchi.App.Tests;

public sealed class ParityPerformanceGateContractTests
{
    [Fact]
    public void Task12_gate_defines_repeatable_old_vs_new_benchmark_and_windows_validation()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "parity.yml");
        var benchmarkPath = Path.Combine(repoRoot, "scripts", "benchmark-parity.ps1");
        var checklistPath = Path.Combine(repoRoot, "docs", "validation", "task12-parity-performance.md");

        Assert.True(File.Exists(workflowPath), $"Missing Task 12 validation workflow: {workflowPath}");
        Assert.True(File.Exists(benchmarkPath), $"Missing Task 12 benchmark harness: {benchmarkPath}");
        Assert.True(File.Exists(checklistPath), $"Missing Task 12 validation checklist: {checklistPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test", workflow, StringComparison.Ordinal);
        Assert.Contains("benchmark-parity.ps1", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("task12-validation", workflow, StringComparison.OrdinalIgnoreCase);

        var benchmark = File.ReadAllText(benchmarkPath);
        Assert.Contains("Runs = 5", benchmark, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReferenceCommand", benchmark, StringComparison.Ordinal);
        Assert.Contains("CandidateCommand", benchmark, StringComparison.Ordinal);
        Assert.Contains("startup", benchmark, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workingSet", benchmark, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inference", benchmark, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("median", benchmark, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("json", benchmark, StringComparison.OrdinalIgnoreCase);

        var checklist = File.ReadAllText(checklistPath);
        Assert.Contains("Windows x64", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows ARM64", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Double-Ctrl", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clipboard", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Translate", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rewrite", checklist, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5", checklist, StringComparison.Ordinal);
    }

    [Fact]
    public void Task12_workflow_collects_real_old_vs_new_benchmark_evidence()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "parity.yml");
        var referenceRunnerPath = Path.Combine(repoRoot, "scripts", "benchmark-reference.ps1");
        var candidateRunnerPath = Path.Combine(repoRoot, "scripts", "benchmark-candidate.ps1");

        Assert.True(File.Exists(referenceRunnerPath), $"Missing legacy benchmark runner: {referenceRunnerPath}");
        Assert.True(File.Exists(candidateRunnerPath), $"Missing Avalonia benchmark runner: {candidateRunnerPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("repository: dhhieu113pro/butchi", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark-reference.ps1", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark-candidate.ps1", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark-parity.ps1", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Runs 5", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark.json", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actions/upload-artifact", workflow, StringComparison.OrdinalIgnoreCase);

        var referenceRunner = File.ReadAllText(referenceRunnerPath);
        Assert.Contains("startup", referenceRunner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workingSet", referenceRunner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inference", referenceRunner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Write-Output '{", referenceRunner, StringComparison.OrdinalIgnoreCase);

        var candidateRunner = File.ReadAllText(candidateRunnerPath);
        Assert.Contains("startup", candidateRunner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workingSet", candidateRunner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inference", candidateRunner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Write-Output '{", candidateRunner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Task12_benchmark_evidence_matches_the_approved_performance_gate_schema()
    {
        var repoRoot = FindRepositoryRoot();
        var benchmarkPath = Path.Combine(repoRoot, "scripts", "benchmark-parity.ps1");
        var referenceRunnerPath = Path.Combine(repoRoot, "scripts", "benchmark-reference.ps1");
        var candidateRunnerPath = Path.Combine(repoRoot, "scripts", "benchmark-candidate.ps1");

        var benchmark = File.ReadAllText(benchmarkPath);
        var referenceRunner = File.ReadAllText(referenceRunnerPath);
        var candidateRunner = File.ReadAllText(candidateRunnerPath);

        foreach (var metric in new[]
        {
            "selectionToPopover",
            "popoverToDispatch",
            "firstToken",
            "tokensPerSecond",
            "ram",
            "vram"
        })
        {
            Assert.Contains(metric, benchmark, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(metric, referenceRunner, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(metric, candidateRunner, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("ConvertFrom-Json", referenceRunner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConvertFrom-Json", candidateRunner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Task12_workflow_enforces_performance_gate_and_publishes_summaries()
    {
        var repoRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "parity.yml");
        var evaluatorPath = Path.Combine(repoRoot, "scripts", "evaluate-performance-gate.ps1");

        Assert.True(File.Exists(evaluatorPath), $"Missing Task 12 performance gate evaluator: {evaluatorPath}");

        var workflow = File.ReadAllText(workflowPath);
        var evaluator = File.ReadAllText(evaluatorPath);

        Assert.Contains("evaluate-performance-gate.ps1", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark.json", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("performance-summary.json", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("performance-summary.md", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actions/upload-artifact", workflow, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("benchmark.json", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("performance-summary.json", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("performance-summary.md", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selectionToPopover", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("popoverToDispatch", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("firstToken", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tokensPerSecond", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ram", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vram", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit 1", evaluator, StringComparison.OrdinalIgnoreCase);
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
