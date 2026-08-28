using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Butchi.Platform.Windows.Selection;

public sealed class WindowsClipboardSelectionSource : IClipboardSelectionSource
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const byte VkControl = 0x11;
    private const byte VkC = 0x43;
    private const uint KeyEventKeyUp = 0x0002;

    public async ValueTask<string?> CaptureAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        SendCtrlC();
        await Task.Delay(60, cancellationToken);
        return await GetClipboardTextAsync(cancellationToken);
    }

    public ValueTask<string?> GetClipboardTextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return ValueTask.FromResult<string?>(null);

        if (!OpenClipboardWithRetry())
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the clipboard for reading.");

        try
        {
            if (!IsClipboardFormatAvailable(CfUnicodeText))
                return ValueTask.FromResult<string?>(null);

            var handle = GetClipboardData(CfUnicodeText);
            if (handle == 0)
                return ValueTask.FromResult<string?>(null);

            var pointer = GlobalLock(handle);
            if (pointer == 0)
                return ValueTask.FromResult<string?>(null);

            try
            {
                return ValueTask.FromResult<string?>(Marshal.PtrToStringUni(pointer));
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public ValueTask SetClipboardTextAsync(string? text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return ValueTask.CompletedTask;

        if (!OpenClipboardWithRetry())
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the clipboard for writing.");

        try
        {
            if (!EmptyClipboard())
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to clear the clipboard.");

            if (text is null)
                return ValueTask.CompletedTask;

            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            var handle = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            if (handle == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to allocate clipboard memory.");

            var pointer = GlobalLock(handle);
            if (pointer == 0)
            {
                GlobalFree(handle);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to lock clipboard memory.");
            }

            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            if (SetClipboardData(CfUnicodeText, handle) == 0)
            {
                GlobalFree(handle);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to restore clipboard text.");
            }

            return ValueTask.CompletedTask;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool OpenClipboardWithRetry()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (OpenClipboard(0))
                return true;
            Thread.Sleep(10);
        }

        return false;
    }

    private static void SendCtrlC()
    {
        keybd_event(VkControl, 0, 0, 0);
        keybd_event(VkC, 0, 0, 0);
        keybd_event(VkC, 0, KeyEventKeyUp, 0);
        keybd_event(VkControl, 0, KeyEventKeyUp, 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memory);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
}
