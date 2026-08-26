using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Butchi.App.Management;
using Butchi.App.Popover;
using Butchi.App.Screenshots;
using Butchi.App.Tray;

namespace Butchi.App;

public sealed class App : Application, IApplicationShutdown
{
    private TrayIcons? _trayIcons;
    private TrayIcon? _trayIcon;

    public PopoverWindow? PopoverWindow { get; private set; }
    public ManagementWindow? ManagementWindow { get; private set; }
    public TrayCommandRouter? TrayRouter { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ManagementWindow = new ManagementWindow(new ManagementShellViewModel());

            if (ScreenshotRequest.TryParse(Program.StartupArgs, out var screenshotRequest))
            {
                ScreenshotRunner.Run(screenshotRequest!, ManagementWindow, Shutdown);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            PopoverWindow = new PopoverWindow(new PopoverViewModel());
            TrayRouter = new TrayCommandRouter(ManagementWindow, this);
            ConfigureTray(TrayRouter);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Shutdown()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayIcons = null;

        PopoverWindow?.Destroy();
        PopoverWindow = null;

        if (ManagementWindow is { } management)
        {
            management.Hide();
            ManagementWindow = null;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ConfigureTray(TrayCommandRouter router)
    {
        var menu = new NativeMenu
        {
            Item("Settings", TrayCommand.OpenSettings, router),
            Item("History", TrayCommand.OpenHistory, router),
            Item("Models", TrayCommand.OpenModels, router),
            Item("Status", TrayCommand.OpenStatus, router),
            new NativeMenuItemSeparator(),
            Item("Exit", TrayCommand.Exit, router)
        };

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Butchi",
            Menu = menu
        };

        _trayIcons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, _trayIcons);
    }

    private static NativeMenuItem Item(string header, TrayCommand command, TrayCommandRouter router)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => router.Execute(command);
        return item;
    }
}
