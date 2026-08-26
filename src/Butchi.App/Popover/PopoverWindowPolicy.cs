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
    public Guid InstanceId { get; } = Guid.NewGuid();
    public bool IsVisible { get; private set; }
    public bool IsDisposed { get; private set; }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;

    public bool HandleEscape()
    {
        Hide();
        return true;
    }

    public void Dispose()
    {
        IsVisible = false;
        IsDisposed = true;
    }
}
