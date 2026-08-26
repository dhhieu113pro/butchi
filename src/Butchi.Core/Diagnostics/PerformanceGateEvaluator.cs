namespace Butchi.Core.Diagnostics;

public sealed record PerformanceSnapshot(
    double SelectionToPopoverMilliseconds,
    double PopoverToDispatchMilliseconds,
    double FirstTokenMilliseconds,
    double TokensPerSecond,
    double RamMegabytes,
    double VramMegabytes)
{
    public double SelectionToFirstTokenMilliseconds =>
        SelectionToPopoverMilliseconds + PopoverToDispatchMilliseconds + FirstTokenMilliseconds;
}

public sealed record PerformanceGateFailure(string Metric, double Actual, double Limit);

public sealed record PerformanceGateResult(IReadOnlyList<PerformanceGateFailure> Failures)
{
    public bool Passed => Failures.Count == 0;
}

public static class PerformanceGateEvaluator
{
    private const double SelectionToPopoverLimitMilliseconds = 50;
    private const double PopoverToDispatchLimitMilliseconds = 30;
    private const double AllowedRegressionFactor = 1.05;
    private const double MinimumThroughputFactor = 0.95;
    private const double AllowedResourceFactor = 1.10;
    private const double RequiredTotalPathImprovementFactor = 0.85;

    public static PerformanceGateResult Evaluate(PerformanceSnapshot baseline, PerformanceSnapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        var failures = new List<PerformanceGateFailure>();

        if (candidate.SelectionToPopoverMilliseconds >= SelectionToPopoverLimitMilliseconds)
        {
            failures.Add(new("selection-to-popover", candidate.SelectionToPopoverMilliseconds, SelectionToPopoverLimitMilliseconds));
        }

        if (candidate.PopoverToDispatchMilliseconds >= PopoverToDispatchLimitMilliseconds)
        {
            failures.Add(new("popover-to-dispatch", candidate.PopoverToDispatchMilliseconds, PopoverToDispatchLimitMilliseconds));
        }

        var firstTokenLimit = baseline.FirstTokenMilliseconds * AllowedRegressionFactor;
        var totalPathImprovementLimit = baseline.SelectionToFirstTokenMilliseconds * RequiredTotalPathImprovementFactor;
        if (candidate.FirstTokenMilliseconds > firstTokenLimit &&
            candidate.SelectionToFirstTokenMilliseconds > totalPathImprovementLimit)
        {
            failures.Add(new("first-token", candidate.FirstTokenMilliseconds, firstTokenLimit));
        }

        var throughputFloor = baseline.TokensPerSecond * MinimumThroughputFactor;
        if (candidate.TokensPerSecond < throughputFloor)
        {
            failures.Add(new("tokens-per-second", candidate.TokensPerSecond, throughputFloor));
        }

        var ramLimit = baseline.RamMegabytes * AllowedResourceFactor;
        if (candidate.RamMegabytes > ramLimit)
        {
            failures.Add(new("ram", candidate.RamMegabytes, ramLimit));
        }

        var vramLimit = baseline.VramMegabytes * AllowedResourceFactor;
        if (candidate.VramMegabytes > vramLimit)
        {
            failures.Add(new("vram", candidate.VramMegabytes, vramLimit));
        }

        return new PerformanceGateResult(failures);
    }
}
