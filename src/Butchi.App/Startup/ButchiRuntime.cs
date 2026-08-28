using Avalonia;
using Avalonia.Controls;
using Butchi.App.Branding;
using Butchi.App.Management;
using Butchi.App.Popover;
using Butchi.App.Tray;
using Butchi.App.Windows;
using Butchi.Core.Actions;
using Butchi.Core.Configuration;

namespace Butchi.App.Startup;

public interface IButchiRuntime : IAsyncDisposable
{
    bool IsTrayStarted { get; }
    void StartTray();
}

public interface IButchiRuntimeFactory
{
    ValueTask<IButchiRuntime> CreateAsync(AppConfig config, CancellationToken cancellationToken);
}

public sealed class ButchiRuntime(
    Application application,
    ManagementWindow managementWindow,
    PopoverWindow popoverWindow,
    WindowsInteractionRuntime interaction,
    PopoverActionController popoverActionController,
    TextActionScheduler scheduler,
    IApplicationShutdown shutdown) : IButchiRuntime
{
    private TrayIcons? _trayIcons;
    private TrayIcon? _trayIcon;
    private int _disposed;

    public ManagementWindow ManagementWindow { get; } = managementWindow;
    public PopoverWindow PopoverWindow { get; } = popoverWindow;
    public TrayCommandRouter TrayRouter { get; } = new(managementWindow, shutdown);
    public bool IsTrayStarted { get; private set; }

    public void StartTray()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (IsTrayStarted) return;
        var menu = new NativeMenu
        {
            Item("Settings", TrayCommand.OpenSettings),
            Item("History", TrayCommand.OpenHistory),
            Item("Models", TrayCommand.OpenModels),
            Item("Status", TrayCommand.OpenStatus),
            new NativeMenuItemSeparator(),
            Item("Exit", TrayCommand.Exit)
        };
        _trayIcon = new TrayIcon
        {
            Icon = BrandAssets.CreateWindowIcon(),
            ToolTipText = "Butchi",
            Menu = menu,
            IsVisible = true
        };
        _trayIcons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(application, _trayIcons);
        interaction.Start();
        IsTrayStarted = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        interaction.Dispose();
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayIcons = null;
        IsTrayStarted = false;
        PopoverWindow.Destroy();
        ManagementWindow.Hide();
        await popoverActionController.DisposeAsync();
        await scheduler.DisposeAsync();
    }

    private NativeMenuItem Item(string header, TrayCommand command)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => TrayRouter.Execute(command);
        return item;
    }
}
