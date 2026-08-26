namespace Butchi.App.Popover;

public readonly record struct PopoverSize(double Width, double Height);
public readonly record struct PopoverPoint(double X, double Y);
public readonly record struct PopoverRect(double X, double Y, double Width, double Height);

public static class PopoverGeometry
{
    private const double CursorOffset = 12;

    public static PopoverSize CalculateSize(
        double width,
        double contentHeight,
        double minimumHeight,
        double maximumHeight)
    {
        var height = Math.Clamp(contentHeight, minimumHeight, maximumHeight);
        return new PopoverSize(width, height);
    }

    public static PopoverPoint PlaceNearCursor(
        double cursorX,
        double cursorY,
        double width,
        double height,
        PopoverRect workingArea)
    {
        var x = cursorX + CursorOffset;
        var y = cursorY + CursorOffset;

        var maxX = workingArea.X + Math.Max(0, workingArea.Width - width);
        var maxY = workingArea.Y + Math.Max(0, workingArea.Height - height);

        x = Math.Clamp(x, workingArea.X, maxX);
        y = Math.Clamp(y, workingArea.Y, maxY);

        return new PopoverPoint(x, y);
    }
}
