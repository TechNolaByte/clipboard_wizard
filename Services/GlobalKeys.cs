using System.Runtime.InteropServices;

namespace ClipboardWizard.Services;

/// <summary>
/// A shared, refcounted low-level keyboard hook (WH_KEYBOARD_LL). Installed only while a consumer
/// holds it (Hawk / Clipboard Cycle), so there's no always-on global hook. Never suppresses keys —
/// it just reports them via <see cref="Key"/> so consumers can react to global Ctrl+V / Escape even
/// when the app has no focus. Must be Acquired/Released on the UI thread (needs a message pump).
/// </summary>
public static class GlobalKeys
{
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int VK_V = 0x56;
    public const int VK_ESCAPE = 0x1B;
    private const int WH_KEYBOARD_LL = 13;
    private const int VK_CONTROL = 0x11;

    private static int _refs;
    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc;

    /// <summary>Raised for each key event: (message, virtual-key, isCtrlDown). Runs on the UI thread.</summary>
    public static event Action<int, int, bool>? Key;

    public static void Acquire()
    {
        if (_refs++ == 0)
        {
            _proc = Callback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }
    }

    public static void Release()
    {
        if (_refs > 0 && --_refs == 0)
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            _proc = null;
        }
    }

    private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            var vk = Marshal.ReadInt32(lParam); // vkCode is the first field of KBDLLHOOKSTRUCT
            var ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            Key?.Invoke(msg, vk, ctrl);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
