using Butchi.App.Screenshots;
using Xunit;

namespace Butchi.App.Tests;

public sealed class ScreenshotModeTests
{
    [Fact]
    public void TryParse_returns_false_when_flag_is_absent()
    {
        var parsed = ScreenshotRequest.TryParse(["--other"], out var request);

        Assert.False(parsed);
        Assert.Null(request);
    }

    [Fact]
    public void TryParse_requires_output_path()
    {
        Assert.Throws<ArgumentException>(() =>
            ScreenshotRequest.TryParse(["--screenshot"], out _));
    }

    [Fact]
    public void TryParse_defaults_to_management_settings_page()
    {
        var parsed = ScreenshotRequest.TryParse(
            ["--screenshot", "artifacts/ui/settings.png"],
            out var request);

        Assert.True(parsed);
        Assert.NotNull(request);
        Assert.Equal("artifacts/ui/settings.png", request.OutputPath);
        Assert.Equal(Management.ManagementPage.Settings, request.Page);
        Assert.Equal(1280, request.Width);
        Assert.Equal(800, request.Height);
    }

    [Fact]
    public void TryParse_accepts_page_and_dimensions()
    {
        var parsed = ScreenshotRequest.TryParse(
            ["--screenshot", "history.png", "--page", "history", "--width", "1440", "--height", "900"],
            out var request);

        Assert.True(parsed);
        Assert.NotNull(request);
        Assert.Equal(Management.ManagementPage.History, request.Page);
        Assert.Equal(1440, request.Width);
        Assert.Equal(900, request.Height);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public void TryParse_rejects_unknown_page(string page)
    {
        Assert.Throws<ArgumentException>(() =>
            ScreenshotRequest.TryParse(
                ["--screenshot", "out.png", "--page", page],
                out _));
    }
}
