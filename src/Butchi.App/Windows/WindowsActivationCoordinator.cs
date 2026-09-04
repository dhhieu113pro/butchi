using Butchi.App.Popover;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;
using Butchi.Platform.Windows.Actions;
using Butchi.Platform.Windows.Pointer;
using Butchi.Platform.Windows.Selection;

namespace Butchi.App.Windows;

public interface IWindowsPopoverView
{
    void SetSelectionInput(string input, AppConfig config);
    void SetPosition(double x, double y);
    void ShowPersistent();
}

public sealed class WindowsActivationCoordinator
{
    private const double PopoverWidth = PopoverWindow.ExpandedWidth;
    private const double PopoverHeight = 360;
    private readonly IWindowsSelectionReader _selectionReader;
    private readonly IWindowsPointerContext _pointerContext;
    private readonly IWindowsPopoverView _popoverView;
    private readonly IWindowsPasteTarget _pasteTarget;
    private readonly Func<CancellationToken, ValueTask<AppConfig>> _loadConfig;

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
        : this(
            selectionReader,
            pointerContext,
            popoverView,
            pasteTarget,
            _ => ValueTask.FromResult(AppConfig.Default))
    {
    }

    public WindowsActivationCoordinator(
        IWindowsSelectionReader selectionReader,
        IWindowsPointerContext pointerContext,
        IWindowsPopoverView popoverView,
        IWindowsPasteTarget pasteTarget,
        Func<CancellationToken, ValueTask<AppConfig>> loadConfig)
    {
        ArgumentNullException.ThrowIfNull(selectionReader);
        ArgumentNullException.ThrowIfNull(pointerContext);
        ArgumentNullException.ThrowIfNull(popoverView);
        ArgumentNullException.ThrowIfNull(pasteTarget);
        ArgumentNullException.ThrowIfNull(loadConfig);

        _selectionReader = selectionReader;
        _pointerContext = pointerContext;
        _popoverView = popoverView;
        _pasteTarget = pasteTarget;
        _loadConfig = loadConfig;
    }

    public async ValueTask<bool> ActivateAsync(CancellationToken cancellationToken)
    {
        var config = await _loadConfig(cancellationToken).ConfigureAwait(false);
        if (AutomationRules.GetEnabledActions(config).Count == 0)
            return false;

        _pasteTarget.CaptureForegroundWindow();

        var selected = await _selectionReader.ReadSelectedTextAsync(cancellationToken).ConfigureAwait(false);
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

        _popoverView.SetSelectionInput(selected, config);
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
