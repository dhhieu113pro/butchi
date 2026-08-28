using Butchi.App.Popover;
using Butchi.Platform.Windows.Actions;
using Butchi.Platform.Windows.Pointer;
using Butchi.Platform.Windows.Selection;

namespace Butchi.App.Windows;

public interface IWindowsPopoverView
{
    void SetSelectionInput(string input);
    void SetPosition(double x, double y);
    void ShowPersistent();
}

public sealed class WindowsActivationCoordinator
{
    private const double PopoverWidth = 420;
    private const double PopoverHeight = 360;
    private readonly IWindowsSelectionReader _selectionReader;
    private readonly IWindowsPointerContext _pointerContext;
    private readonly IWindowsPopoverView _popoverView;
    private readonly IWindowsPasteTarget _pasteTarget;

    public WindowsActivationCoordinator(
        IWindowsSelectionReader selectionReader,
        IWindowsPointerContext pointerContext,
        IWindowsPopoverView popoverView)
        : this(selectionReader, pointerContext, popoverView, NoOpPasteTarget.Instance)
    {
    }

    public WindowsActivationCoordinator(
        IWindowsSelectionReader selectionReader,
        IWindowsPointerContext pointerContext,
        IWindowsPopoverView popoverView,
        IWindowsPasteTarget pasteTarget)
    {
        _selectionReader = selectionReader;
        _pointerContext = pointerContext;
        _popoverView = popoverView;
        _pasteTarget = pasteTarget;
    }

    public async ValueTask<bool> ActivateAsync(CancellationToken cancellationToken)
    {
        _pasteTarget.CaptureForegroundWindow();

        var selected = await _selectionReader.ReadSelectedTextAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(selected))
            return false;

        var pointer = _pointerContext.GetCurrent();
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

        _popoverView.SetSelectionInput(selected);
        _popoverView.SetPosition(position.X, position.Y);
        _popoverView.ShowPersistent();
        return true;
    }

    private sealed class NoOpPasteTarget : IWindowsPasteTarget
    {
        public static NoOpPasteTarget Instance { get; } = new();
        public void CaptureForegroundWindow() { }
    }
}
