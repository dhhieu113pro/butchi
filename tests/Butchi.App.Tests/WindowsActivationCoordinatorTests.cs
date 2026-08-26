using Butchi.App.Popover;
using Butchi.App.Windows;
using Butchi.Platform.Windows.Pointer;
using Butchi.Platform.Windows.Selection;
using Xunit;

namespace Butchi.App.Tests;

public sealed class WindowsActivationCoordinatorTests
{
    [Fact]
    public async Task Trigger_reads_selection_positions_and_shows_popover()
    {
        var selection = new FakeSelection("hello");
        var pointer = new FakePointer(new PointerContextSnapshot(1880, 1040, new NativeRect(0, 0, 1920, 1040)));
        var view = new FakePopoverView();
        var coordinator = new WindowsActivationCoordinator(selection, pointer, view);

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
            new FakePopoverView());

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

    private sealed class FakePopoverView : IWindowsPopoverView
    {
        public string? Input { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool ShowCalled { get; private set; }
        public void SetSelectionInput(string input) => Input = input;
        public void SetPosition(double x, double y) { X = x; Y = y; }
        public void ShowPersistent() => ShowCalled = true;
    }
}
