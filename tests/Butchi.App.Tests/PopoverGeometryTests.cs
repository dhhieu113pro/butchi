using Butchi.App.Popover;
using Xunit;

namespace Butchi.App.Tests;

public sealed class PopoverGeometryTests
{
    [Fact]
    public void Desired_size_grows_with_content_but_is_bounded()
    {
        var compact = PopoverGeometry.CalculateSize(420, 120, 360, 760);
        var tall = PopoverGeometry.CalculateSize(420, 2_000, 360, 760);

        Assert.Equal(420, compact.Width);
        Assert.InRange(compact.Height, 360, 760);
        Assert.Equal(760, tall.Height);
    }

    [Fact]
    public void Placement_is_centered_near_top_of_working_area()
    {
        var point = PopoverGeometry.PlaceNearCursor(
            cursorX: 500,
            cursorY: 300,
            width: 420,
            height: 360,
            workingArea: new PopoverRect(0, 0, 1920, 1080));

        Assert.Equal(750, point.X);
        Assert.Equal(16, point.Y);
    }

    [Fact]
    public void Placement_uses_active_monitor_working_area_origin()
    {
        var point = PopoverGeometry.PlaceNearCursor(
            cursorX: -900,
            cursorY: 500,
            width: 420,
            height: 360,
            workingArea: new PopoverRect(-1920, 40, 1920, 1040));

        Assert.Equal(-1170, point.X);
        Assert.Equal(56, point.Y);
    }

    [Fact]
    public void Compact_and_expanded_widths_keep_the_same_horizontal_center()
    {
        var method = typeof(PopoverGeometry).GetMethod("CenteredX");
        Assert.NotNull(method);

        var center = 960d;
        var compactX = (double)method.Invoke(null, new object[] { center, 420d })!;
        var expandedX = (double)method.Invoke(null, new object[] { center, 760d })!;

        Assert.Equal(750, compactX);
        Assert.Equal(580, expandedX);
        Assert.Equal(center, compactX + 210);
        Assert.Equal(center, expandedX + 380);
    }
}
