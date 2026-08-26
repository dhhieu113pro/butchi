using Butchi.Core.Diagnostics;
using Xunit;

namespace Butchi.Core.Tests;

public sealed class BenchmarkComparisonTests
{
    [Fact]
    public void Compares_baseline_and_candidate_medians_through_the_performance_gate()
    {
        var baselineRuns = Enumerable.Repeat(
            new PerformanceSnapshot(60, 24, 100, 20, 1000, 2000),
            5);
        var candidateRuns = Enumerable.Repeat(
            new PerformanceSnapshot(45, 20, 103, 19.5, 1080, 2180),
            5);

        var comparison = BenchmarkComparison.FromRuns(baselineRuns, candidateRuns);

        Assert.Equal(5, comparison.Baseline.RunCount);
        Assert.Equal(5, comparison.Candidate.RunCount);
        Assert.Equal(100, comparison.Baseline.Median.FirstTokenMilliseconds);
        Assert.Equal(103, comparison.Candidate.Median.FirstTokenMilliseconds);
        Assert.True(comparison.Gate.Passed);
    }

    [Fact]
    public void Reports_gate_failures_from_candidate_medians()
    {
        var baselineRuns = Enumerable.Repeat(
            new PerformanceSnapshot(45, 20, 100, 20, 1000, 2000),
            5);
        var candidateRuns = Enumerable.Repeat(
            new PerformanceSnapshot(55, 35, 110, 18, 1200, 2300),
            5);

        var comparison = BenchmarkComparison.FromRuns(baselineRuns, candidateRuns);

        Assert.False(comparison.Gate.Passed);
        Assert.Contains(comparison.Gate.Failures, failure => failure.Metric == "selection-to-popover");
        Assert.Contains(comparison.Gate.Failures, failure => failure.Metric == "popover-to-dispatch");
        Assert.Contains(comparison.Gate.Failures, failure => failure.Metric == "first-token");
        Assert.Contains(comparison.Gate.Failures, failure => failure.Metric == "tokens-per-second");
        Assert.Contains(comparison.Gate.Failures, failure => failure.Metric == "ram");
        Assert.Contains(comparison.Gate.Failures, failure => failure.Metric == "vram");
    }
}
