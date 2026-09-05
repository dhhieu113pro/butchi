using Butchi.Core.Vision;
using Butchi.Platform.Windows.Capture;
using Xunit;

namespace Butchi.Platform.Windows.Tests;

public sealed class WindowsScreenCaptureServiceTests
{
    [Fact]
    public async Task Capture_rejects_non_positive_bounds()
    {
        var service = new WindowsScreenCaptureService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.CaptureAsync(new ScreenCaptureBounds(0, 0, 0, 1), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.CaptureAsync(new ScreenCaptureBounds(0, 0, 1, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Capture_honors_pre_cancelled_token()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var service = new WindowsScreenCaptureService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.CaptureAsync(new ScreenCaptureBounds(0, 0, 1, 1), cts.Token));
    }

    [Fact]
    public async Task Capture_returns_bgra_pixels_with_opaque_alpha()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var service = new WindowsScreenCaptureService();

        var frame = await service.CaptureAsync(
            new ScreenCaptureBounds(0, 0, 2, 2),
            CancellationToken.None);

        Assert.Equal(2, frame.Width);
        Assert.Equal(2, frame.Height);
        Assert.Equal(8, frame.Stride);
        Assert.Equal(16, frame.BgraPixels.Length);
        Assert.All(
            Enumerable.Range(0, frame.Width * frame.Height),
            pixel => Assert.Equal(255, frame.BgraPixels[(pixel * 4) + 3]));
    }
}
