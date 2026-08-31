using Butchi.App.Popover;
using Butchi.App.Windows;
using Butchi.Core.Configuration;
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

    [Fact]
    public async Task Disabled_actions_do_not_show_popover()
    {
        var config = AppConfig.Default with { TranslateEnabled = false, RewriteEnabled = false };
        var view = new FakePopoverView();
        var pasteTarget = new FakePasteTarget();
        var coordinator = new WindowsActivationCoordinator(
            new FakeSelection("hello"),
            new FakePointer(new PointerContextSnapshot(100, 100, new NativeRect(0, 0, 1920, 1080))),
            view,
            pasteTarget,
            _ => ValueTask.FromResult(config));

        var shown = await coordinator.ActivateAsync(CancellationToken.None);

        Assert.False(shown);
        Assert.False(view.ShowCalled);
        Assert.False(pasteTarget.CaptureCalled);
    }

    [Fact]
    public async Task Trigger_reloads_action_configuration_for_each_activation()
    {
        var config = AppConfig.Default with
        {
            TranslateEnabled = true,
            RewriteEnabled = false,
            TargetLanguage = "Japanese"
        };
        var view = new FakePopoverView();
        var coordinator = new WindowsActivationCoordinator(
            new FakeSelection("hello"),
            new FakePointer(new PointerContextSnapshot(100, 100, new NativeRect(0, 0, 1920, 1080))),
            view,
            new FakePasteTarget(),
            _ => ValueTask.FromResult(config));

        Assert.True(await coordinator.ActivateAsync(CancellationToken.None));
        Assert.NotNull(view.Config);
        Assert.True(view.Config!.TranslateEnabled);
        Assert.False(view.Config.RewriteEnabled);
        Assert.Equal("Japanese", view.Config.TargetLanguage);

        config = config with { TranslateEnabled = false, RewriteEnabled = true };

        Assert.True(await coordinator.ActivateAsync(CancellationToken.None));
        Assert.NotNull(view.Config);
        Assert.False(view.Config!.TranslateEnabled);
        Assert.True(view.Config.RewriteEnabled);
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
        public bool CaptureCalled { get; private set; }

        public void CaptureForegroundWindow()
        {
            CaptureCalled = true;
            events?.Add("capture-target");
        }
    }

    private sealed class FakePopoverView(List<string>? events = null) : IWindowsPopoverView
    {
        public string? Input { get; private set; }
        public AppConfig? Config { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool ShowCalled { get; private set; }
        public void SetSelectionInput(string input, AppConfig config)
        {
            Input = input;
            Config = config;
        }
        public void SetPosition(double x, double y) { X = x; Y = y; }
        public void ShowPersistent()
        {
            events?.Add("show-popover");
            ShowCalled = true;
        }
    }
}
