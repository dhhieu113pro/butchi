using System.Security;
using Butchi.Infrastructure.AutoStart;
using Xunit;

namespace Butchi.Infrastructure.Tests;

public sealed class AutoStartFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "butchi-autostart-tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _executablePath = Path.Combine(
        Path.GetTempPath(),
        "Butchi Folder",
        "butchi executable");

    [Fact]
    public async Task Mac_service_writes_verifies_and_removes_only_its_launch_agent()
    {
        var service = new MacOsAutoStartService(_root, _executablePath);

        Assert.False(await service.GetEnabledAsync(CancellationToken.None));
        await service.EnableAsync(CancellationToken.None);

        var path = Path.Combine(_root, "io.github.dhhieu113pro.butchi.plist");
        var xml = await File.ReadAllTextAsync(path);
        Assert.Contains("<key>Label</key>", xml, StringComparison.Ordinal);
        Assert.Contains("io.github.dhhieu113pro.butchi", xml, StringComparison.Ordinal);
        Assert.Contains(SecurityElement.Escape(_executablePath) ?? _executablePath, xml, StringComparison.Ordinal);
        Assert.Contains("<key>RunAtLoad</key>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepAlive", xml, StringComparison.Ordinal);
        Assert.True(await service.GetEnabledAsync(CancellationToken.None));

        await service.DisableAsync(CancellationToken.None);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Mac_service_reports_disabled_for_malformed_or_foreign_registration()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "io.github.dhhieu113pro.butchi.plist");
        var service = new MacOsAutoStartService(_root, _executablePath);

        await File.WriteAllTextAsync(path, "not xml");
        Assert.False(await service.GetEnabledAsync(CancellationToken.None));

        await File.WriteAllTextAsync(
            path,
            """<?xml version="1.0"?><plist><dict><key>ProgramArguments</key><array><string>/tmp/other</string></array></dict></plist>""");
        Assert.False(await service.GetEnabledAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Linux_service_writes_verifies_and_removes_its_desktop_entry()
    {
        var service = new LinuxAutoStartService(_root, _executablePath);

        Assert.False(await service.GetEnabledAsync(CancellationToken.None));
        await service.EnableAsync(CancellationToken.None);

        var path = Path.Combine(_root, "autostart", "butchi.desktop");
        var desktop = await File.ReadAllTextAsync(path);
        Assert.Contains("[Desktop Entry]", desktop, StringComparison.Ordinal);
        Assert.Contains("Type=Application", desktop, StringComparison.Ordinal);
        Assert.Contains("Name=Butchi", desktop, StringComparison.Ordinal);
        Assert.Contains("X-GNOME-Autostart-enabled=true", desktop, StringComparison.Ordinal);
        Assert.True(await service.GetEnabledAsync(CancellationToken.None));

        await service.DisableAsync(CancellationToken.None);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Linux_service_reports_disabled_for_malformed_hidden_or_foreign_registration()
    {
        var directory = Path.Combine(_root, "autostart");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "butchi.desktop");
        var service = new LinuxAutoStartService(_root, _executablePath);

        await File.WriteAllTextAsync(path, "not a desktop entry");
        Assert.False(await service.GetEnabledAsync(CancellationToken.None));

        await File.WriteAllTextAsync(
            path,
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Butchi\n" +
            "Exec=\"/tmp/other\"\n" +
            "X-GNOME-Autostart-enabled=true\n");
        Assert.False(await service.GetEnabledAsync(CancellationToken.None));

        await service.EnableAsync(CancellationToken.None);
        await File.AppendAllTextAsync(path, "Hidden=true\n");
        Assert.False(await service.GetEnabledAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Linux_service_quotes_spaces_and_escapes_literal_percent_field_codes()
    {
        var executable = "/opt/Butchi Folder/100%/butchi";
        var service = new LinuxAutoStartService(_root, executable);

        await service.EnableAsync(CancellationToken.None);

        var desktop = await File.ReadAllTextAsync(Path.Combine(_root, "autostart", "butchi.desktop"));
        Assert.Contains("Exec=\"/opt/Butchi Folder/100%%/butchi\"", desktop, StringComparison.Ordinal);
        Assert.True(await service.GetEnabledAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
