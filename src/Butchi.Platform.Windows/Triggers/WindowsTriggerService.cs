namespace Butchi.Platform.Windows.Triggers;

public sealed class CtrlPressEventArgs(
    DateTimeOffset timestamp,
    bool hasOtherModifier,
    bool isRepeat) : EventArgs
{
    public DateTimeOffset Timestamp { get; } = timestamp;
    public bool HasOtherModifier { get; } = hasOtherModifier;
    public bool IsRepeat { get; } = isRepeat;
}

public interface IKeyboardHookSource : IDisposable
{
    event EventHandler<CtrlPressEventArgs>? CtrlPressed;
    void Start();
    void Stop();
}

public sealed class WindowsTriggerService : IDisposable
{
    private readonly IKeyboardHookSource _hook;
    private readonly DoubleCtrlDetector _detector;
    private bool _started;
    private bool _disposed;

    public WindowsTriggerService(IKeyboardHookSource hook, TimeSpan doubleCtrlWindow)
    {
        _hook = hook;
        _detector = new DoubleCtrlDetector(doubleCtrlWindow);
        _hook.CtrlPressed += OnCtrlPressed;
    }

    public event EventHandler? Triggered;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return;

        _hook.Start();
        _started = true;
    }

    private void OnCtrlPressed(object? sender, CtrlPressEventArgs e)
    {
        if (_detector.ObserveCtrlPress(e.Timestamp, e.HasOtherModifier, e.IsRepeat))
            Triggered?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _hook.CtrlPressed -= OnCtrlPressed;
        if (_started)
            _hook.Stop();
        _hook.Dispose();
    }
}
