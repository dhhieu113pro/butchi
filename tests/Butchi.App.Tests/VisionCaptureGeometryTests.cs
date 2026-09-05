using Avalonia;
using Butchi.App.Vision;

namespace Butchi.App.Tests;

public sealed class VisionCaptureGeometryTests
{
    [Fact]
    public void Normalize_supports_dragging_in_any_direction()
    {
        var actual = ScreenSelectionGeometry.Normalize(
            new Point(180, 120),
            new Point(20, 40));

        Assert.Equal(new Rect(20, 40, 160, 80), actual);
    }

    [Fact]
    public void ToPixelRect_converts_logical_selection_using_monitor_scale()
    {
        var actual = ScreenSelectionGeometry.ToPixelRect(
            new Rect(10, 20, 100, 50),
            1.5);

        Assert.Equal(new PixelRect(15, 30, 150, 75), actual);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(4.99, 20)]
    [InlineData(20, 4.99)]
    public void IsUsable_rejects_tiny_selections(double width, double height)
    {
        Assert.False(ScreenSelectionGeometry.IsUsable(new Rect(0, 0, width, height)));
    }

    [Fact]
    public void IsUsable_accepts_a_real_selection()
    {
        Assert.True(ScreenSelectionGeometry.IsUsable(new Rect(0, 0, 5, 5)));
    }
}
