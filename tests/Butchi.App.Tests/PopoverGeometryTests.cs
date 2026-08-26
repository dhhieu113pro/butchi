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
    public void Placement_prefers_below_and_right_of_cursor()
    {
        var point = PopoverGeometry.PlaceNearCursor(
            cursorX: 500,
            cursorY: 300,
            width: 420,
            height: 360,
            workingArea: new PopoverRect(0, 0, 1920, 1080));

        Assert.True(point.X > 500);
        Assert.True(point.Y > 300);
    }

    [Fact]
    public void Placement_clamps_to_working_area()
    {
        var point = PopoverGeometry.PlaceNearCursor(
            cursorX: 1900,
            cursorY: 1050,
            width: 420,
            height: 360,
            workingArea: new PopoverRect(0, 0, 1920, 1080));

        Assert.InRange(point.X, 0, 1500);
        Assert.InRange(point.Y, 0, 720);
    }
}
