using Butchi.App.Popover;
using Butchi.App.Windows;
using Butchi.Platform.Windows.Actions;
using Butchi.Platform.Windows.Pointer;
using Butchi.Platform.Windows.Selection;
using Xunit;

namespace Butchi.App.Tests;

public sealed class WindowsActivationCoordinatorTests
{
    [Fact]
    public async Task Trigger_captures_paste_target_before_showing_popover()
    {
        var events = new List<string>();
        var selection = new FakeSelection("hello");
        var pointer = new FakePointer(new PointerContextSnapshot(1880, 1040, new NativeRect(0, 0, 1920, 1040)));
        var view = new FakePopoverView(events);
        var pasteTarget = new FakePasteTarget(events);
        var coordinator = new WindowsActivationCoordinator(selection, pointer, view, pasteTarget);

        await coordinator.ActivateAsync(CancellationToken.None);

        Assert.Equal(new[] { "capture-target", "show-popover" }, events);
    }

    [Fact]
    public async Task Trigger_reads_selection_positions_and_shows_popover()
    {
        var selection = new FakeSelection("hello");
        var pointer = new FakePointer(new PointerContextSnapshot(1880, 1040, new NativeRect(0, 0, 1920, 1040)));
        var view = new FakePopoverView();
        var coordinator = new WindowsActivationCoordinator(selection, pointer, view, new FakePasteTarget());

        await coordinator.ActivateAsync(CancellationToken.None);

        Assert.Equal("hello", view.Input);
        Assert.True(view.ShowCalled);
        Assert.InRange(view.X, 0, 1500);
        Assert.InRange(view.Y, 0, 680);
    }

    [Fact]
    public async Task Empty_selection_does_not_show_popover()
    {
        var coordinator = new WindowsActivationCoordinator(
            new FakeSelection("  "),
            new FakePointer(new PointerContextSnapshot(1, 1, new NativeRect(0, 0, 1920, 1080))),
            new FakePopoverView(),
            new FakePasteTarget());

        var shown = await coordinator.ActivateAsync(CancellationToken.None);

        Assert.False(shown);
    }

    private sealed class FakeSelection(string? text) : IWindowsSelectionReader
    {
        public ValueTask<string?> ReadSelectedTextAsync(CancellationToken cancellationToken) => ValueTask.FromResult(text);
    }

    private sealed class FakePointer(PointerContextSnapshot snapshot) : IWindowsPointerContext
    {
        public PointerContextSnapshot GetCurrent() => snapshot;
    }

    private sealed class FakePasteTarget(List<string>? events = null) : IWindowsPasteTarget
    {
        public void CaptureForegroundWindow() => events?.Add("capture-target");
    }

    private sealed class FakePopoverView(List<string>? events = null) : IWindowsPopoverView
    {
        public string? Input { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool ShowCalled { get; private set; }
        public void SetSelectionInput(string input) => Input = input;
        public void SetPosition(double x, double y) { X = x; Y = y; }
        public void ShowPersistent()
        {
            events?.Add("show-popover");
            ShowCalled = true;
        }
    }
}
