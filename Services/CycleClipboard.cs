using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ClipboardWizard.Services;

/// <summary>
/// Cycle Clipboard: split the clipboard text into fragments (non-empty lines), put the first on the
/// clipboard, then advance to the next fragment on each Ctrl+V. A low-level keyboard hook detects
/// the paste; it's installed only while a cycle is active (no always-on background hook).
/// </summary>
public static class CycleClipboard
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_V = 0x56;
    private const int VK_CONTROL = 0x11;

    private static string[] _fragments = Array.Empty<string>();
    private static int _index;      // fragment currently on the clipboard (ready to paste)
    private static bool _vDown;     // debounce key auto-repeat
    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc; // keep the delegate alive

    public static bool Active { get; private set; }
    public static int Position => _index + 1;
    public static int Total => _fragments.Length;

    /// <summary>Raised on start/advance/stop so the tray can refresh.</summary>
    public static Action? Changed;

    public static void Start(string text)
    {
        Stop();

        _fragments = text.Replace("\r\n", "\n").Split('\n')
            .Select(s => s.TrimEnd())
            .Where(s => s.Trim().Length > 0)
            .ToArray();
        if (_fragments.Length == 0)
            return;

        _index = 0;
        Active = true;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        LoadCurrent();
        Changed?.Invoke();
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
        _vDown = false;
        if (Active)
        {
            Active = false;
            Changed?.Invoke();
        }
    }

    private static void LoadCurrent()
    {
        AppState.SuppressNextClipboardChange?.Invoke();
        ClipboardWriter.SetText(_fragments[_index]);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            var vk = Marshal.ReadInt32(lParam); // vkCode is the first field of KBDLLHOOKSTRUCT

            if (msg == WM_KEYUP && vk == VK_V)
            {
                _vDown = false;
            }
            else if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && vk == VK_V && !_vDown
                     && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
            {
                _vDown = true;
                ScheduleAdvance(); // don't suppress — let the paste happen, then advance
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static void ScheduleAdvance()
    {
        // Give the paste a moment to consume the current fragment before we load the next one.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); Advance(); };
        timer.Start();
    }

    private static void Advance()
    {
        if (!Active)
            return;

        if (_index + 1 < _fragments.Length)
        {
            _index++;
            LoadCurrent();
            Changed?.Invoke();
        }
        else
        {
            Stop(); // last fragment has been pasted
        }
    }

    // ---- interop ----
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
