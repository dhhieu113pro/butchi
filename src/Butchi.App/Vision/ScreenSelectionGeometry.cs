using Avalonia;

namespace Butchi.App.Vision;

public static class ScreenSelectionGeometry
{
    public const double MinimumSelectionSize = 5;

    public static Rect Normalize(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        return new Rect(x, y, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
    }

    public static PixelRect ToPixelRect(Rect selection, double scale)
    {
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));

        var left = (int)Math.Floor(selection.X * scale);
        var top = (int)Math.Floor(selection.Y * scale);
        var right = (int)Math.Ceiling(selection.Right * scale);
        var bottom = (int)Math.Ceiling(selection.Bottom * scale);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public static bool IsUsable(Rect selection) =>
        selection.Width >= MinimumSelectionSize &&
        selection.Height >= MinimumSelectionSize;
}
