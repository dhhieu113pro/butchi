using Butchi.App.Diagnostics;
using Xunit;

namespace Butchi.App.Tests;

public sealed class InstalledAppProbeTests
{
    [Fact]
    public void TryParse_accepts_release_probe_with_output_path()
    {
        var parsed = ReleaseProbe.TryParse(["--release-probe", "probe.json"], out var outputPath);

        Assert.True(parsed);
        Assert.Equal("probe.json", outputPath);
    }

    [Fact]
    public void TryParse_rejects_missing_release_probe_output_path()
    {
        Assert.False(ReleaseProbe.TryParse(["--release-probe"], out _));
    }

    [Fact]
    public void Result_contains_only_privacy_safe_startup_markers()
    {
        var result = ReleaseProbeResult.Success("Butchi.Test", "0.1.0.0");

        Assert.True(result.Success);
        Assert.True(result.CompositionHealthy);
        Assert.Equal("Butchi.Test", result.PackageIdentity);
        Assert.Equal("0.1.0.0", result.PackageVersion);
        Assert.Null(result.SelectedText);
        Assert.Null(result.PromptContent);
        Assert.Null(result.HistoryContent);
    }
}
