using Butchi.Core.Diagnostics;
using Xunit;

namespace Butchi.Core.Tests;

public sealed class BenchmarkSummaryTests
{
    [Fact]
    public void Requires_at_least_five_equivalent_runs()
    {
        var runs = Enumerable.Repeat(
            new PerformanceSnapshot(40, 20, 100, 20, 1000, 2000),
            4);

        Assert.Throws<ArgumentException>(() => BenchmarkSummary.FromRuns(runs));
    }

    [Fact]
    public void Uses_median_values_across_equivalent_warm_runs()
    {
        var runs = new[]
        {
            new PerformanceSnapshot(44, 24, 104, 18, 1040, 2040),
            new PerformanceSnapshot(40, 20, 100, 20, 1000, 2000),
            new PerformanceSnapshot(42, 22, 102, 22, 1020, 2020),
            new PerformanceSnapshot(38, 18, 98, 24, 980, 1980),
            new PerformanceSnapshot(46, 26, 106, 16, 1060, 2060),
        };

        var summary = BenchmarkSummary.FromRuns(runs);

        Assert.Equal(42, summary.Median.SelectionToPopoverMilliseconds);
        Assert.Equal(22, summary.Median.PopoverToDispatchMilliseconds);
        Assert.Equal(102, summary.Median.FirstTokenMilliseconds);
        Assert.Equal(20, summary.Median.TokensPerSecond);
        Assert.Equal(1020, summary.Median.RamMegabytes);
        Assert.Equal(2020, summary.Median.VramMegabytes);
        Assert.Equal(5, summary.RunCount);
    }
}
