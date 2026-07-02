using System.Linq;
using System.Windows.Threading;

namespace ClipboardWizard.Services;

/// <summary>
/// Clipboard Cycle: split the clipboard text into fragments (non-empty lines), put the first on the
/// clipboard, and advance to the next on each Ctrl+V (detected via the shared GlobalKeys hook, only
/// while cycling). Ends on a global Escape or when something else is copied.
/// </summary>
public static class ClipboardCycle
{
    private static string[] _fragments = Array.Empty<string>();
    private static int _index;   // fragment currently on the clipboard (pastes next)
    private static bool _vDown;  // debounce key auto-repeat

    public static bool Active { get; private set; }
    public static int Total => _fragments.Length;

    /// <summary>Fragments remaining including the one currently on the clipboard.</summary>
    public static int Remaining => Active ? _fragments.Length - _index : 0;

    /// <summary>Short preview of the fragment that will paste next.</summary>
    public static string NextPreview =>
        (Active && _index < _fragments.Length) ? Snippet(_fragments[_index]) : "";

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
        GlobalKeys.Key += OnKey;
        GlobalKeys.Acquire();
        LoadCurrent();
        Changed?.Invoke();
    }

    public static void Stop()
    {
        if (Active)
        {
            Active = false;
            GlobalKeys.Key -= OnKey;
            GlobalKeys.Release();
            Changed?.Invoke();
        }
        _vDown = false;
    }

    private static void LoadCurrent()
    {
        AppState.SuppressNextClipboardChange?.Invoke();
        ClipboardWriter.SetText(_fragments[_index]);
    }

    private static void OnKey(int msg, int vk, bool ctrl)
    {
        if (msg == GlobalKeys.WM_KEYUP && vk == GlobalKeys.VK_V)
        {
            _vDown = false;
            return;
        }
        if (msg == GlobalKeys.WM_KEYDOWN || msg == GlobalKeys.WM_SYSKEYDOWN)
        {
            if (vk == GlobalKeys.VK_ESCAPE)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(Stop));
            }
            else if (vk == GlobalKeys.VK_V && !_vDown && ctrl)
            {
                _vDown = true;
                ScheduleAdvance(); // let the paste happen, then advance
            }
        }
    }

    private static void ScheduleAdvance()
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is null)
            return;
        disp.BeginInvoke(new Action(() =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (_, _) => { timer.Stop(); Advance(); };
            timer.Start();
        }));
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
            Stop();
        }
    }

    internal static string Snippet(string s)
    {
        var one = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return one.Length <= 10 ? one : one[..10] + "…";
    }
}
