using System.Runtime.InteropServices;

namespace Butchi.Platform.Windows.Actions;

public sealed class WindowsPasteSender : IWindowsPasteSender
{
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyEventKeyUp = 0x0002;

    public Task SendPasteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return Task.CompletedTask;

        keybd_event(VkControl, 0, 0, 0);
        keybd_event(VkV, 0, 0, 0);
        keybd_event(VkV, 0, KeyEventKeyUp, 0);
        keybd_event(VkControl, 0, KeyEventKeyUp, 0);
        return Task.CompletedTask;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
}
