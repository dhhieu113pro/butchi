using Butchi.App.Management;
using Butchi.App.Tray;
using Xunit;

namespace Butchi.App.Tests;

public sealed class TrayRoutingTests
{
    [Theory]
    [InlineData(TrayCommand.OpenSettings, ManagementPage.Settings)]
    [InlineData(TrayCommand.OpenHistory, ManagementPage.History)]
    [InlineData(TrayCommand.OpenModels, ManagementPage.Models)]
    [InlineData(TrayCommand.OpenStatus, ManagementPage.Status)]
    public void Management_commands_select_page_and_show_window(TrayCommand command, ManagementPage expected)
    {
        var window = new FakeManagementWindow();
        var shutdown = new FakeShutdown();
        var router = new TrayCommandRouter(window, shutdown);

        router.Execute(command);

        Assert.Equal(expected, window.SelectedPage);
        Assert.Equal(1, window.ShowCalls);
        Assert.Equal(0, shutdown.Calls);
    }

    [Fact]
    public void Exit_routes_to_explicit_shutdown()
    {
        var window = new FakeManagementWindow();
        var shutdown = new FakeShutdown();
        var router = new TrayCommandRouter(window, shutdown);

        router.Execute(TrayCommand.Exit);

        Assert.Equal(1, shutdown.Calls);
        Assert.Equal(0, window.ShowCalls);
    }

    private sealed class FakeManagementWindow : IManagementWindowHost
    {
        public ManagementPage? SelectedPage { get; private set; }
        public int ShowCalls { get; private set; }
        public void Show(ManagementPage page)
        {
            SelectedPage = page;
            ShowCalls++;
        }
    }

    private sealed class FakeShutdown : IApplicationShutdown
    {
        public int Calls { get; private set; }
        public void Shutdown() => Calls++;
    }
}
