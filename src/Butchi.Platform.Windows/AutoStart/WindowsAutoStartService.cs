using System.Runtime.Versioning;
using Butchi.Core.Platform;

namespace Butchi.Platform.Windows.AutoStart;

public sealed class WindowsAutoStartService : IAutoStartService
{
    private readonly IWindowsPackageIdentity _packageIdentity;
    private readonly IAutoStartService _packaged;
    private readonly IAutoStartService _unpackaged;

    internal WindowsAutoStartService(
        IWindowsPackageIdentity packageIdentity,
        IAutoStartService packaged,
        IAutoStartService unpackaged)
    {
        _packageIdentity = packageIdentity;
        _packaged = packaged;
        _unpackaged = unpackaged;
    }

    private IAutoStartService Current => _packageIdentity.IsPackaged ? _packaged : _unpackaged;

    public ValueTask<bool> GetEnabledAsync(CancellationToken cancellationToken) =>
        Current.GetEnabledAsync(cancellationToken);

    public ValueTask EnableAsync(CancellationToken cancellationToken) =>
        Current.EnableAsync(cancellationToken);

    public ValueTask DisableAsync(CancellationToken cancellationToken) =>
        Current.DisableAsync(cancellationToken);

    [SupportedOSPlatform("windows10.0.14393.0")]
    public static WindowsAutoStartService CreateDefault(string executablePath) => new(
        new WindowsPackageIdentity(),
        new WindowsStartupTaskAutoStartService(new WinRtStartupTaskAccessor("ButchiStartup")),
        new WindowsRunKeyAutoStartService(new CurrentUserRunKeyStore("Butchi"), executablePath));
}
