using System.Runtime.InteropServices;

namespace ClipboardWizard.Services;

/// <summary>
/// A shared, refcounted low-level mouse hook (WH_MOUSE_LL), the mouse twin of <see cref="GlobalKeys"/>.
/// Installed only while a consumer holds it, never suppresses input — it just reports button presses
/// with their screen coordinates so a window that deliberately doesn't take focus (the passive popup)
/// can still notice a click landing somewhere else. Acquire/Release on the UI thread (needs a pump).
/// </summary>
public static class GlobalMouse
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;

    private static int _refs;
    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelMouseProc? _proc;

    /// <summary>Raised on any mouse button press: (screenX, screenY). Runs on the UI thread.</summary>
    public static event Action<int, int>? ButtonDown;

    public static void Acquire()
    {
        if (_refs++ == 0)
        {
            _proc = Callback;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
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
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
            {
                // MSLLHOOKSTRUCT starts with the screen-space POINT.
                var x = Marshal.ReadInt32(lParam, 0);
                var y = Marshal.ReadInt32(lParam, 4);
                ButtonDown?.Invoke(x, y);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
