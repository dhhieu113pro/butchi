using Butchi.Platform.Windows.Triggers;
using Xunit;

namespace Butchi.Platform.Windows.Tests;

public sealed class DoubleCtrlDetectorTests
{
    [Fact]
    public void Two_clean_ctrl_presses_within_window_trigger_once()
    {
        var detector = new DoubleCtrlDetector(TimeSpan.FromMilliseconds(350));
        var t0 = DateTimeOffset.UnixEpoch;

        Assert.False(detector.ObserveCtrlPress(t0, hasOtherModifier: false, isRepeat: false));
        Assert.True(detector.ObserveCtrlPress(t0.AddMilliseconds(250), hasOtherModifier: false, isRepeat: false));
        Assert.False(detector.ObserveCtrlPress(t0.AddMilliseconds(260), hasOtherModifier: false, isRepeat: true));
    }

    [Fact]
    public void Slow_second_press_does_not_trigger()
    {
        var detector = new DoubleCtrlDetector(TimeSpan.FromMilliseconds(350));
        var t0 = DateTimeOffset.UnixEpoch;

        Assert.False(detector.ObserveCtrlPress(t0, false, false));
        Assert.False(detector.ObserveCtrlPress(t0.AddMilliseconds(500), false, false));
    }

    [Fact]
    public void Modifier_combination_breaks_the_sequence()
    {
        var detector = new DoubleCtrlDetector(TimeSpan.FromMilliseconds(350));
        var t0 = DateTimeOffset.UnixEpoch;

        Assert.False(detector.ObserveCtrlPress(t0, false, false));
        Assert.False(detector.ObserveCtrlPress(t0.AddMilliseconds(100), true, false));
        Assert.False(detector.ObserveCtrlPress(t0.AddMilliseconds(200), false, false));
    }
}
