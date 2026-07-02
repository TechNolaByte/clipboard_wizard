using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ClipboardWizard.Services;

/// <summary>
/// Registers a global hotkey (Ctrl+Win+C) via a hidden tool window and raises <see cref="Activated"/>
/// when it's pressed — so the popup can be summoned on demand, not just on clipboard change.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xB001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_C = 0x43;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private HwndSource? _source;

    /// <summary>Raised on the UI thread when Ctrl+Win+C is pressed.</summary>
    public event EventHandler? Activated;

    /// <summary>True if the hotkey registered successfully (false if the combo was already taken).</summary>
    public bool Registered { get; private set; }

    public void Start()
    {
        // A hidden top-level tool window (WM_HOTKEY isn't reliably delivered to message-only windows).
        var parameters = new HwndSourceParameters("ClipboardWizard.Hotkeys")
        {
            Width = 0,
            Height = 0,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = 0, // not WS_VISIBLE
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        Registered = RegisterHotKey(_source.Handle, HotkeyId, MOD_CONTROL | MOD_WIN | MOD_NOREPEAT, VK_C);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam == HotkeyId)
        {
            handled = true;
            Activated?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null)
            return;
        if (Registered)
            UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
