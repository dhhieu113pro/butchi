using Butchi.App.Popover;
using Butchi.Platform.Windows.Pointer;
using Butchi.Platform.Windows.Selection;

namespace Butchi.App.Windows;

public interface IWindowsPopoverView
{
    void SetSelectionInput(string input);
    void SetPosition(double x, double y);
    void ShowPersistent();
}

public sealed class WindowsActivationCoordinator(
    IWindowsSelectionReader selectionReader,
    IWindowsPointerContext pointerContext,
    IWindowsPopoverView popoverView)
{
    private const double PopoverWidth = 420;
    private const double PopoverHeight = 360;

    public async ValueTask<bool> ActivateAsync(CancellationToken cancellationToken)
    {
        var selected = await selectionReader.ReadSelectedTextAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(selected))
            return false;

        var pointer = pointerContext.GetCurrent();
        var position = PopoverGeometry.PlaceNearCursor(
            pointer.CursorX,
            pointer.CursorY,
            PopoverWidth,
            PopoverHeight,
            new PopoverRect(
                pointer.WorkingArea.X,
                pointer.WorkingArea.Y,
                pointer.WorkingArea.Width,
                pointer.WorkingArea.Height));

        popoverView.SetSelectionInput(selected);
        popoverView.SetPosition(position.X, position.Y);
        popoverView.ShowPersistent();
        return true;
    }
}
