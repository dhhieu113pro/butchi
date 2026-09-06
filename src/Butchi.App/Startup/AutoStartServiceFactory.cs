using Butchi.Core.Platform;
using Butchi.Infrastructure.AutoStart;
using Butchi.Platform.Windows.AutoStart;

namespace Butchi.App.Startup;

internal enum AutoStartPlatform
{
    Windows,
    MacOs,
    Linux,
    Unsupported
}

internal static class AutoStartServiceFactory
{
    public static IAutoStartService Create(
        string executablePath,
        string userProfile,
        string? xdgConfigHome = null)
    {
        var platform = OperatingSystem.IsWindows()
            ? AutoStartPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? AutoStartPlatform.MacOs
                : OperatingSystem.IsLinux()
                    ? AutoStartPlatform.Linux
                    : AutoStartPlatform.Unsupported;

        return CreateForPlatform(platform, executablePath, userProfile, xdgConfigHome);
    }

    internal static IAutoStartService CreateForPlatform(
        AutoStartPlatform platform,
        string executablePath,
        string userProfile,
        string? xdgConfigHome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);

        return platform switch
        {
            AutoStartPlatform.Windows => WindowsAutoStartService.CreateDefault(executablePath),
            AutoStartPlatform.MacOs => new MacOsAutoStartService(
                Path.Combine(userProfile, "Library", "LaunchAgents"),
                executablePath),
            AutoStartPlatform.Linux => new LinuxAutoStartService(
                string.IsNullOrWhiteSpace(xdgConfigHome)
                    ? Path.Combine(userProfile, ".config")
                    : xdgConfigHome,
                executablePath),
            _ => new UnsupportedAutoStartService()
        };
    }

    private sealed class UnsupportedAutoStartService : IAutoStartService
    {
        public ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask EnableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException(new PlatformNotSupportedException(
                "Launch at login is not supported on this operating system."));
        }

        public ValueTask DisableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException(new PlatformNotSupportedException(
                "Launch at login is not supported on this operating system."));
        }
    }
}
