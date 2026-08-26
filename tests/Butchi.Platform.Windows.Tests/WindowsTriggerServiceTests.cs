using Butchi.Platform.Windows.Triggers;
using Xunit;

namespace Butchi.Platform.Windows.Tests;

public sealed class WindowsTriggerServiceTests
{
    [Fact]
    public void Starts_hook_once_and_raises_trigger_on_double_ctrl()
    {
        var hook = new FakeKeyboardHook();
        using var service = new WindowsTriggerService(hook, TimeSpan.FromMilliseconds(350));
        var raised = 0;
        service.Triggered += (_, _) => raised++;

        service.Start();
        service.Start();
        hook.RaiseCtrl(DateTimeOffset.UnixEpoch, false, false);
        hook.RaiseCtrl(DateTimeOffset.UnixEpoch.AddMilliseconds(200), false, false);

        Assert.Equal(1, hook.StartCalls);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Dispose_stops_hook()
    {
        var hook = new FakeKeyboardHook();
        var service = new WindowsTriggerService(hook, TimeSpan.FromMilliseconds(350));
        service.Start();

        service.Dispose();

        Assert.Equal(1, hook.StopCalls);
    }

    private sealed class FakeKeyboardHook : IKeyboardHookSource
    {
        public event EventHandler<CtrlPressEventArgs>? CtrlPressed;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public void Start() => StartCalls++;
        public void Stop() => StopCalls++;
        public void Dispose() { }
        public void RaiseCtrl(DateTimeOffset timestamp, bool otherModifier, bool repeat) =>
            CtrlPressed?.Invoke(this, new CtrlPressEventArgs(timestamp, otherModifier, repeat));
    }
}
