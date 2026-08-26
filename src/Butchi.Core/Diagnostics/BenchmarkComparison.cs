namespace Butchi.Core.Diagnostics;

public sealed record BenchmarkComparison(
    BenchmarkSummary Baseline,
    BenchmarkSummary Candidate,
    PerformanceGateResult Gate)
{
    public static BenchmarkComparison FromRuns(
        IEnumerable<PerformanceSnapshot> baselineRuns,
        IEnumerable<PerformanceSnapshot> candidateRuns)
    {
        ArgumentNullException.ThrowIfNull(baselineRuns);
        ArgumentNullException.ThrowIfNull(candidateRuns);

        var baseline = BenchmarkSummary.FromRuns(baselineRuns);
        var candidate = BenchmarkSummary.FromRuns(candidateRuns);
        var gate = PerformanceGateEvaluator.Evaluate(baseline.Median, candidate.Median);

        return new BenchmarkComparison(baseline, candidate, gate);
    }
}
