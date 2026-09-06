using Butchi.App.Startup;
using Butchi.Core.Platform;
using Butchi.Infrastructure.AutoStart;
using Butchi.Platform.Windows.AutoStart;
using Xunit;

namespace Butchi.App.Tests;

public sealed class AutoStartCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "butchi-autostart-composition",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Factory_returns_file_services_with_expected_user_paths()
    {
        var executable = Path.Combine(_root, "Butchi App", "butchi");
        var macHome = Path.Combine(_root, "mac-home");
        var linuxHome = Path.Combine(_root, "linux-home");
        var xdg = Path.Combine(_root, "xdg");

        var mac = AutoStartServiceFactory.CreateForPlatform(
            AutoStartPlatform.MacOs,
            executable,
            macHome,
            null);
        var linux = AutoStartServiceFactory.CreateForPlatform(
            AutoStartPlatform.Linux,
            executable,
            linuxHome,
            xdg);

        Assert.IsType<MacOsAutoStartService>(mac);
        Assert.IsType<LinuxAutoStartService>(linux);

        await mac.EnableAsync(CancellationToken.None);
        await linux.EnableAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(
            macHome,
            "Library",
            "LaunchAgents",
            "io.github.dhhieu113pro.butchi.plist")));
        Assert.True(File.Exists(Path.Combine(xdg, "autostart", "butchi.desktop")));
    }

    [Fact]
    public async Task Linux_factory_falls_back_to_dot_config_when_xdg_is_missing()
    {
        var home = Path.Combine(_root, "linux-fallback-home");
        var service = AutoStartServiceFactory.CreateForPlatform(
            AutoStartPlatform.Linux,
            Path.Combine(_root, "butchi"),
            home,
            "   ");

        await service.EnableAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(home, ".config", "autostart", "butchi.desktop")));
    }

    [Fact]
    public void Factory_returns_windows_dispatcher_without_touching_startup_state()
    {
        var service = AutoStartServiceFactory.CreateForPlatform(
            AutoStartPlatform.Windows,
            @"C:\Program Files\Butchi\butchi.exe",
            @"C:\Users\quinn",
            null);

        Assert.IsType<WindowsAutoStartService>(service);
    }

    [Fact]
    public async Task Unsupported_platform_reports_disabled_and_rejects_changes()
    {
        IAutoStartService service = AutoStartServiceFactory.CreateForPlatform(
            AutoStartPlatform.Unsupported,
            "/tmp/butchi",
            "/tmp/home",
            null);

        Assert.False(await service.GetEnabledAsync(CancellationToken.None));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await service.EnableAsync(CancellationToken.None));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await service.DisableAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
