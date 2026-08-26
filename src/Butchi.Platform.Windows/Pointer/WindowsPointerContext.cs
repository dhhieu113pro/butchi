namespace Butchi.Platform.Windows.Pointer;

public readonly record struct NativePoint(int X, int Y);
public readonly record struct NativeRect(int X, int Y, int Width, int Height);
public readonly record struct PointerContextSnapshot(int CursorX, int CursorY, NativeRect WorkingArea);

public interface IWindowsPointerSource
{
    NativePoint GetCursorPosition();
    NativeRect GetWorkingArea(NativePoint cursor);
}

public interface IWindowsPointerContext
{
    PointerContextSnapshot GetCurrent();
}

public sealed class WindowsPointerContext(IWindowsPointerSource source) : IWindowsPointerContext
{
    public PointerContextSnapshot GetCurrent()
    {
        var cursor = source.GetCursorPosition();
        var rect = source.GetWorkingArea(cursor);
        return new PointerContextSnapshot(cursor.X, cursor.Y, rect);
    }
}
