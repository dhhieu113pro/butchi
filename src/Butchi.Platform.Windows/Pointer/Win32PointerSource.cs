using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Butchi.Platform.Windows.Pointer;

public sealed class Win32PointerSource : IWindowsPointerSource
{
    private const uint MonitorDefaultToNearest = 2;

    public NativePoint GetCursorPosition()
    {
        if (!OperatingSystem.IsWindows())
            return new NativePoint(0, 0);

        if (!GetCursorPos(out var point))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read the current cursor position.");

        return new NativePoint(point.X, point.Y);
    }

    public NativeRect GetWorkingArea(NativePoint cursor)
    {
        if (!OperatingSystem.IsWindows())
            return new NativeRect(0, 0, 1920, 1080);

        var monitor = MonitorFromPoint(new Point(cursor.X, cursor.Y), MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read the monitor working area.");

        return new NativeRect(
            info.Work.Left,
            info.Work.Top,
            info.Work.Right - info.Work.Left,
            info.Work.Bottom - info.Work.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Point(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
