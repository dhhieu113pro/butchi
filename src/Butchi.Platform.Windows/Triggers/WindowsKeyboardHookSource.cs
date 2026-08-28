using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Butchi.Platform.Windows.Triggers;

public sealed class WindowsKeyboardHookSource : IKeyboardHookSource
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkControl = 0x11;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly HashSet<int> _pressedCtrlKeys = [];
    private readonly LowLevelKeyboardProc _callback;
    private nint _hook;
    private int _disposed;

    public WindowsKeyboardHookSource() => _callback = HookCallback;

    public event EventHandler<CtrlPressEventArgs>? CtrlPressed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_hook != 0 || !OperatingSystem.IsWindows())
            return;

        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
        if (_hook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install Butchi keyboard hook.");
    }

    public void Stop()
    {
        if (_hook == 0)
            return;

        UnhookWindowsHookEx(_hook);
        _hook = 0;
        _pressedCtrlKeys.Clear();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Stop();
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var message = unchecked((int)wParam);
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var virtualKey = unchecked((int)data.VirtualKeyCode);
            if (IsCtrl(virtualKey))
            {
                if (message is WmKeyDown or WmSysKeyDown)
                {
                    var repeat = !_pressedCtrlKeys.Add(virtualKey);
                    CtrlPressed?.Invoke(this, new CtrlPressEventArgs(
                        DateTimeOffset.UtcNow,
                        HasOtherModifier(),
                        repeat));
                }
                else if (message is WmKeyUp or WmSysKeyUp)
                {
                    _pressedCtrlKeys.Remove(virtualKey);
                }
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool IsCtrl(int virtualKey) => virtualKey is VkControl or VkLControl or VkRControl;

    private static bool HasOtherModifier() =>
        IsDown(VkShift) || IsDown(VkMenu) || IsDown(VkLWin) || IsDown(VkRWin);

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KbdLlHookStruct
    {
        public readonly uint VirtualKeyCode;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
}
