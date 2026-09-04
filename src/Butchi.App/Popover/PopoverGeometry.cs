namespace Butchi.App.Popover;

public readonly record struct PopoverSize(double Width, double Height);
public readonly record struct PopoverPoint(double X, double Y);
public readonly record struct PopoverRect(double X, double Y, double Width, double Height);

public static class PopoverGeometry
{
    private const double TopOffset = 16;

    public static PopoverSize CalculateSize(
        double width,
        double contentHeight,
        double minimumHeight,
        double maximumHeight)
    {
        var height = Math.Clamp(contentHeight, minimumHeight, maximumHeight);
        return new PopoverSize(width, height);
    }

    public static double CenteredX(double centerX, double width) =>
        centerX - (width / 2);

    public static PopoverPoint PlaceNearCursor(
        double cursorX,
        double cursorY,
        double width,
        double height,
        PopoverRect workingArea)
    {
        _ = cursorX;
        _ = cursorY;

        var maxX = workingArea.X + Math.Max(0, workingArea.Width - width);
        var maxY = workingArea.Y + Math.Max(0, workingArea.Height - height);
        var centeredX = CenteredX(
            workingArea.X + (workingArea.Width / 2),
            width);

        var x = Math.Clamp(centeredX, workingArea.X, maxX);
        var y = Math.Clamp(workingArea.Y + TopOffset, workingArea.Y, maxY);

        return new PopoverPoint(x, y);
    }
}
