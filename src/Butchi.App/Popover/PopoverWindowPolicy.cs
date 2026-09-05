namespace Butchi.App.Popover;

public enum PopoverTheme
{
    System,
    Light,
    Dark
}

public sealed record PopoverWindowProfile(
    bool Borderless,
    bool Topmost,
    bool ShowInTaskbar,
    bool CanResize,
    bool UseBoundedScroll)
{
    public static PopoverWindowProfile Default { get; } = new(
        Borderless: true,
        Topmost: true,
        ShowInTaskbar: false,
        CanResize: false,
        UseBoundedScroll: true);
}

public static class PopoverThemePolicy
{
    public static string ToVariantName(PopoverTheme theme) => theme switch
    {
        PopoverTheme.Light => "Light",
        PopoverTheme.Dark => "Dark",
        _ => "Default"
    };
}

public sealed class PopoverWindowController
{
    private static readonly TimeSpan DefaultPointerExitDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultResultIdleDelay = TimeSpan.FromSeconds(8);
    private CancellationTokenSource? _pendingHide;
    private bool _pointerInside;
    private bool _isWorkActive;

    public Guid InstanceId { get; } = Guid.NewGuid();
    public bool IsVisible { get; private set; }
    public bool IsDisposed { get; private set; }
    public bool IsPinned { get; private set; }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        CancelPendingHide();
        _pointerInside = false;
        IsVisible = true;
    }

    public void Hide()
    {
        CancelPendingHide();
        _pointerInside = false;
        _isWorkActive = false;
        IsPinned = false;
        IsVisible = false;
    }

    public void TogglePinned()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        IsPinned = !IsPinned;
        if (IsPinned)
            CancelPendingHide();
    }

    public void HandlePointerEntered()
    {
        _pointerInside = true;
        CancelPendingHide();
    }

    public Task<bool> HandlePointerExitedAsync(TimeSpan? delay = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _pointerInside = false;
        CancelPendingHide();

        if (_isWorkActive || IsPinned)
            return Task.FromResult(false);

        return ScheduleHideAsync(delay ?? DefaultPointerExitDelay);
    }

    public void HandleWorkStarted()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _isWorkActive = true;
        CancelPendingHide();
    }

    public Task<bool> HandleResultCompletedAsync(TimeSpan? delay = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _isWorkActive = false;
        CancelPendingHide();

        if (_pointerInside || IsPinned)
            return Task.FromResult(false);

        return ScheduleHideAsync(delay ?? DefaultResultIdleDelay);
    }

    public bool HandleEscape() => DismissImmediately();

    public bool HandleDeactivated()
    {
        if (IsPinned)
            return false;

        return DismissImmediately();
    }

    public void Dispose()
    {
        CancelPendingHide();
        _pointerInside = false;
        _isWorkActive = false;
        IsPinned = false;
        IsVisible = false;
        IsDisposed = true;
    }

    private bool DismissImmediately()
    {
        Hide();
        return true;
    }

    private async Task<bool> ScheduleHideAsync(TimeSpan delay)
    {
        using var cancellation = new CancellationTokenSource();
        _pendingHide = cancellation;

        try
        {
            await Task.Delay(delay, cancellation.Token);
            if (!ReferenceEquals(_pendingHide, cancellation)) return false;

            _pendingHide = null;
            IsVisible = false;
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(_pendingHide, cancellation))
                _pendingHide = null;
        }
    }

    private void CancelPendingHide()
    {
        var cancellation = Interlocked.Exchange(ref _pendingHide, null);
        cancellation?.Cancel();
    }
}
