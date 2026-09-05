using System.ComponentModel;
using System.Runtime.InteropServices;
using Butchi.Core.Vision;

namespace Butchi.Platform.Windows.Capture;

public sealed class WindowsScreenCaptureService : IScreenCaptureService
{
    private const uint DibRgbColors = 0;
    private const uint Srccopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    public ValueTask<ScreenCaptureFrame> CaptureAsync(
        ScreenCaptureBounds bounds,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Desktop capture is currently available on Windows only.");
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Capture bounds must have positive dimensions.");

        cancellationToken.ThrowIfCancellationRequested();
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw CreateWin32Exception("GetDC");

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
                throw CreateWin32Exception("CreateCompatibleDC");

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = checked((uint)Marshal.SizeOf<BitmapInfoHeader>()),
                    Width = bounds.Width,
                    Height = -bounds.Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = checked((uint)(bounds.Width * bounds.Height * 4))
                }
            };

            bitmap = CreateDIBSection(
                screenDc,
                ref info,
                DibRgbColors,
                out var bits,
                IntPtr.Zero,
                0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                throw CreateWin32Exception("CreateDIBSection");

            previous = SelectObject(memoryDc, bitmap);
            if (previous == IntPtr.Zero)
                throw CreateWin32Exception("SelectObject");

            if (!BitBlt(
                    memoryDc,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    screenDc,
                    bounds.X,
                    bounds.Y,
                    Srccopy | CaptureBlt))
            {
                throw CreateWin32Exception("BitBlt");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var index = 3; index < pixels.Length; index += 4)
                pixels[index] = 255;

            return ValueTask.FromResult(new ScreenCaptureFrame(bounds.Width, bounds.Height, pixels));
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero)
                _ = SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero)
                _ = DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero)
                _ = DeleteDC(memoryDc);
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static Win32Exception CreateWin32Exception(string operation) =>
        new(Marshal.GetLastWin32Error(), $"{operation} failed while capturing the screen.");

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr dc,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr destinationDc,
        int x,
        int y,
        int width,
        int height,
        IntPtr sourceDc,
        int sourceX,
        int sourceY,
        uint operation);
}
