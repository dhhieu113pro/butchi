namespace Butchi.Core.Diagnostics;

public sealed record BenchmarkSummary(PerformanceSnapshot Median, int RunCount)
{
    public static BenchmarkSummary FromRuns(IEnumerable<PerformanceSnapshot> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var snapshots = runs.ToArray();
        if (snapshots.Length < 5)
        {
            throw new ArgumentException("At least five equivalent warm runs are required.", nameof(runs));
        }

        return new BenchmarkSummary(
            new PerformanceSnapshot(
                MedianOf(snapshots.Select(static run => run.SelectionToPopoverMilliseconds)),
                MedianOf(snapshots.Select(static run => run.PopoverToDispatchMilliseconds)),
                MedianOf(snapshots.Select(static run => run.FirstTokenMilliseconds)),
                MedianOf(snapshots.Select(static run => run.TokensPerSecond)),
                MedianOf(snapshots.Select(static run => run.RamMegabytes)),
                MedianOf(snapshots.Select(static run => run.VramMegabytes))),
            snapshots.Length);
    }

    private static double MedianOf(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        var midpoint = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[midpoint]
            : (ordered[midpoint - 1] + ordered[midpoint]) / 2;
    }
}
