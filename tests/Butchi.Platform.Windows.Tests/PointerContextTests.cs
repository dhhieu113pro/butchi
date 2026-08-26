using Butchi.Platform.Windows.Pointer;
using Xunit;

namespace Butchi.Platform.Windows.Tests;

public sealed class PointerContextTests
{
    [Fact]
    public void Maps_cursor_and_working_area_from_native_source()
    {
        var source = new FakePointerSource(
            new NativePoint(1880, 1040),
            new NativeRect(0, 0, 1920, 1040));
        var context = new WindowsPointerContext(source);

        var result = context.GetCurrent();

        Assert.Equal(1880, result.CursorX);
        Assert.Equal(1040, result.CursorY);
        Assert.Equal(0, result.WorkingArea.X);
        Assert.Equal(0, result.WorkingArea.Y);
        Assert.Equal(1920, result.WorkingArea.Width);
        Assert.Equal(1040, result.WorkingArea.Height);
    }

    private sealed class FakePointerSource(NativePoint point, NativeRect rect) : IWindowsPointerSource
    {
        public NativePoint GetCursorPosition() => point;
        public NativeRect GetWorkingArea(NativePoint cursor) => rect;
    }
}
