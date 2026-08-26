namespace Butchi.Platform.Windows.Triggers;

public sealed class DoubleCtrlDetector
{
    private readonly TimeSpan _window;
    private DateTimeOffset? _firstPress;

    public DoubleCtrlDetector(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));

        _window = window;
    }

    public bool ObserveCtrlPress(DateTimeOffset timestamp, bool hasOtherModifier, bool isRepeat)
    {
        if (isRepeat)
            return false;

        if (hasOtherModifier)
        {
            _firstPress = null;
            return false;
        }

        if (_firstPress is { } first && timestamp >= first && timestamp - first <= _window)
        {
            _firstPress = null;
            return true;
        }

        _firstPress = timestamp;
        return false;
    }
}
