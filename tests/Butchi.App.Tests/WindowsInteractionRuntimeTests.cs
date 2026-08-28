using Butchi.App.Popover;
using Butchi.App.Windows;
using Butchi.Platform.Windows.Pointer;
using Butchi.Platform.Windows.Selection;
using Butchi.Platform.Windows.Triggers;
using Xunit;

namespace Butchi.App.Tests;

public sealed class WindowsInteractionRuntimeTests
{
    [Fact]
    public async Task Start_is_idempotent_and_trigger_activates_popover()
    {
        var hook = new FakeKeyboardHook();
        var trigger = new WindowsTriggerService(hook, TimeSpan.FromMilliseconds(350));
        var view = new FakePopoverView();
        var coordinator = new WindowsActivationCoordinator(
            new FakeSelection("selected text"),
            new FakePointer(new PointerContextSnapshot(500, 400, new NativeRect(0, 0, 1920, 1080))),
            view);
        using var runtime = new WindowsInteractionRuntime(trigger, coordinator);

        runtime.Start();
        runtime.Start();
        hook.RaiseCtrl(DateTimeOffset.UnixEpoch);
        hook.RaiseCtrl(DateTimeOffset.UnixEpoch.AddMilliseconds(150));

        await WaitUntilAsync(() => view.ShowCalled);
        Assert.Equal(1, hook.StartCalls);
        Assert.Equal("selected text", view.Input);
    }

    [Fact]
    public void Dispose_stops_trigger_and_prevents_restart()
    {
        var hook = new FakeKeyboardHook();
        var trigger = new WindowsTriggerService(hook, TimeSpan.FromMilliseconds(350));
        var coordinator = new WindowsActivationCoordinator(
            new FakeSelection("selected text"),
            new FakePointer(new PointerContextSnapshot(1, 1, new NativeRect(0, 0, 1920, 1080))),
            new FakePopoverView());
        var runtime = new WindowsInteractionRuntime(trigger, coordinator);
        runtime.Start();

        runtime.Dispose();
        runtime.Dispose();

        Assert.Equal(1, hook.StopCalls);
        Assert.Throws<ObjectDisposedException>(runtime.Start);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < timeout)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FakeKeyboardHook : IKeyboardHookSource
    {
        public event EventHandler<CtrlPressEventArgs>? CtrlPressed;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public void Start() => StartCalls++;
        public void Stop() => StopCalls++;
        public void Dispose() { }
        public void RaiseCtrl(DateTimeOffset timestamp) =>
            CtrlPressed?.Invoke(this, new CtrlPressEventArgs(timestamp, false, false));
    }

    private sealed class FakeSelection(string? text) : IWindowsSelectionReader
    {
        public ValueTask<string?> ReadSelectedTextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(text);
    }

    private sealed class FakePointer(PointerContextSnapshot snapshot) : IWindowsPointerContext
    {
        public PointerContextSnapshot GetCurrent() => snapshot;
    }

    private sealed class FakePopoverView : IWindowsPopoverView
    {
        public string? Input { get; private set; }
        public bool ShowCalled { get; private set; }
        public void SetSelectionInput(string input) => Input = input;
        public void SetPosition(double x, double y) { }
        public void ShowPersistent() => ShowCalled = true;
    }
}
