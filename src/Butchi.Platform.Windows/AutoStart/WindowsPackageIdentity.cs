using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Butchi.Platform.Windows.AutoStart;

internal interface IWindowsPackageIdentity
{
    bool IsPackaged { get; }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsPackageIdentity : IWindowsPackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    public bool IsPackaged
    {
        get
        {
            uint length = 0;
            var result = GetCurrentPackageFullName(ref length, null);
            return result switch
            {
                0 => true,
                ErrorInsufficientBuffer => true,
                AppModelErrorNoPackage => false,
                _ => throw new Win32Exception(result)
            };
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        StringBuilder? packageFullName);
}
