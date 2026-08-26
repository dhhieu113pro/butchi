using Butchi.App.Management;

namespace Butchi.App.Tray;

public enum TrayCommand
{
    OpenSettings,
    OpenHistory,
    OpenModels,
    OpenStatus,
    Exit
}

public interface IManagementWindowHost
{
    void Show(ManagementPage page);
}

public interface IApplicationShutdown
{
    void Shutdown();
}

public sealed class TrayCommandRouter(
    IManagementWindowHost managementWindow,
    IApplicationShutdown applicationShutdown)
{
    public void Execute(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.OpenSettings:
                managementWindow.Show(ManagementPage.Settings);
                break;
            case TrayCommand.OpenHistory:
                managementWindow.Show(ManagementPage.History);
                break;
            case TrayCommand.OpenModels:
                managementWindow.Show(ManagementPage.Models);
                break;
            case TrayCommand.OpenStatus:
                managementWindow.Show(ManagementPage.Status);
                break;
            case TrayCommand.Exit:
                applicationShutdown.Shutdown();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }
}
