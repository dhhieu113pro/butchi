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
    private static readonly TimeSpan DefaultPointerExitDelay = TimeSpan.FromSeconds(1);
    private CancellationTokenSource? _pendingPointerExitHide;

    public Guid InstanceId { get; } = Guid.NewGuid();
    public bool IsVisible { get; private set; }
    public bool IsDisposed { get; private set; }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        CancelPendingPointerExitHide();
        IsVisible = true;
    }

    public void Hide()
    {
        CancelPendingPointerExitHide();
        IsVisible = false;
    }

    public void HandlePointerEntered() => CancelPendingPointerExitHide();

    public async Task<bool> HandlePointerExitedAsync(TimeSpan? delay = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        CancelPendingPointerExitHide();

        using var cancellation = new CancellationTokenSource();
        _pendingPointerExitHide = cancellation;

        try
        {
            await Task.Delay(delay ?? DefaultPointerExitDelay, cancellation.Token);
            if (!ReferenceEquals(_pendingPointerExitHide, cancellation)) return false;

            _pendingPointerExitHide = null;
            IsVisible = false;
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(_pendingPointerExitHide, cancellation))
                _pendingPointerExitHide = null;
        }
    }

    public bool HandleEscape()
    {
        Hide();
        return true;
    }

    public void Dispose()
    {
        CancelPendingPointerExitHide();
        IsVisible = false;
        IsDisposed = true;
    }

    private void CancelPendingPointerExitHide()
    {
        var cancellation = Interlocked.Exchange(ref _pendingPointerExitHide, null);
        cancellation?.Cancel();
    }
}
