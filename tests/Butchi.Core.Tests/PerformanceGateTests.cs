using Butchi.Core.Diagnostics;
using Xunit;

namespace Butchi.Core.Tests;

public sealed class PerformanceGateTests
{
    [Fact]
    public void Accepts_metrics_within_all_approved_thresholds()
    {
        var baseline = new PerformanceSnapshot(60, 24, 100, 20, 1000, 2000);
        var candidate = new PerformanceSnapshot(45, 20, 103, 19.5, 1080, 2180);

        var result = PerformanceGateEvaluator.Evaluate(baseline, candidate);

        Assert.True(result.Passed);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Rejects_slow_ui_dispatch_and_resource_regressions()
    {
        var baseline = new PerformanceSnapshot(60, 20, 100, 20, 1000, 2000);
        var candidate = new PerformanceSnapshot(55, 35, 106, 18.8, 1110, 2210);

        var result = PerformanceGateEvaluator.Evaluate(baseline, candidate);

        Assert.False(result.Passed);
        Assert.Contains(result.Failures, failure => failure.Metric == "selection-to-popover");
        Assert.Contains(result.Failures, failure => failure.Metric == "popover-to-dispatch");
        Assert.Contains(result.Failures, failure => failure.Metric == "tokens-per-second");
        Assert.Contains(result.Failures, failure => failure.Metric == "ram");
        Assert.Contains(result.Failures, failure => failure.Metric == "vram");
    }

    [Fact]
    public void Allows_first_token_regression_only_when_total_path_is_at_least_fifteen_percent_faster()
    {
        var baseline = new PerformanceSnapshot(70, 20, 100, 20, 1000, 2000);
        var fasterTotal = new PerformanceSnapshot(30, 20, 108, 20, 1000, 2000);
        var notFastEnough = new PerformanceSnapshot(55, 20, 108, 20, 1000, 2000);

        Assert.True(PerformanceGateEvaluator.Evaluate(baseline, fasterTotal).Passed);
        Assert.Contains(
            PerformanceGateEvaluator.Evaluate(baseline, notFastEnough).Failures,
            failure => failure.Metric == "first-token");
    }
}
