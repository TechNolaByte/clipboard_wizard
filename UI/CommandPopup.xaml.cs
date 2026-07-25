using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClipboardWizard.Interop;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.UI;

/// <summary>
/// The command menu that appears at the cursor when the clipboard changes. Owns its placement
/// (pixel-accurate, screen-clamped), filtering, and keyboard handling.
/// </summary>
public partial class CommandPopup : Window
{
    /// <summary>
    /// Passive popups ignore global input for this long after opening, so a trailing keystroke or
    /// click from the copy gesture that summoned them can't dismiss them instantly.
    /// </summary>
    private const long PassiveArmDelayMs = 250;

    private ClipboardPayload _payload;
    private readonly CommandContext _context;
    private readonly ObservableCollection<CommandItem> _items;
    private readonly ICollectionView _view;
    // Sticky-focus watchdog: while the popup is open it keeps yanking the foreground back so the next
    // input lands here no matter where you click. Runs only while the popup is up (stopped on close),
    // and never in passive mode.
    private readonly DispatcherTimer _focusGuard;
    // Passive mode: shown without activation (never steals focus). Instead of holding the foreground
    // it watches global input and gets out of the way — any keypress, or a click outside it, closes it.
    private readonly bool _passive;
    private long _armedAt;
    private bool _engaged;
    private System.Drawing.Point _anchor;
    private bool _dismissing;
    private bool _executing;
    private bool _ignoreDeactivate;
    private bool _keysHooked;
    private bool _mouseHooked;

    public CommandPopup(ClipboardPayload payload, IReadOnlyList<IClipboardCommand> commands,
        CommandContext context, bool passive = false)
    {
        InitializeComponent();

        _payload = payload;
        _context = context;
        _passive = passive;
        if (passive)
        {
            // Must be set before Show(): this is what keeps the popup from taking focus as it appears.
            ShowActivated = false;
            HintText.Text = "Esc, any key, or a click away to exit";
        }

        _items = new ObservableCollection<CommandItem>(
            commands.Select((c, i) => new CommandItem { Command = c, Index = i }));

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandItem.Group)));
        // Group order first, then original index — keeps "newest script on top" intact.
        _view.SortDescriptions.Add(new SortDescription(nameof(CommandItem.GroupOrder), ListSortDirection.Ascending));
        _view.SortDescriptions.Add(new SortDescription(nameof(CommandItem.Index), ListSortDirection.Ascending));
        _view.Filter = FilterPredicate;
        CommandList.ItemsSource = _view;

        BuildPreview(payload);
        SelectFirst();

        _focusGuard = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(80) };
        _focusGuard.Tick += (_, _) => GuardFocus();

        Loaded += (_, _) =>
        {
            _armedAt = Environment.TickCount64 + PassiveArmDelayMs;

            if (!_passive)
            {
                ForceForeground();
                _focusGuard.Start();
            }

            // Global keys: in the sticky-focus mode this is a belt-and-braces Escape for when focus
            // somehow ended up elsewhere; in passive mode it's the primary dismissal path (the popup
            // has no focus, so every keystroke arrives here). Mouse presses are only watched in
            // passive mode. Both hooks are refcounted and released on close; the flags keep a repeat
            // Loaded from double-acquiring and leaking an always-on hook.
            if (!_keysHooked)
            {
                _keysHooked = true;
                GlobalKeys.Acquire();
                GlobalKeys.Key += OnGlobalKey;
            }
            if (_passive && !_mouseHooked)
            {
                _mouseHooked = true;
                GlobalMouse.Acquire();
                GlobalMouse.ButtonDown += OnGlobalMouseDown;
            }
        };
        ContentRendered += OnContentRendered;
        // Set the guard the instant any close begins (Escape, focus loss, or an external
        // Close() from App), so the Deactivated that a close raises can't re-enter Close().
        Closing += (_, _) =>
        {
            _dismissing = true;
            _focusGuard.Stop();
            if (_keysHooked)
            {
                _keysHooked = false;
                GlobalKeys.Key -= OnGlobalKey;
                GlobalKeys.Release();
            }
            if (_mouseHooked)
            {
                _mouseHooked = false;
                GlobalMouse.ButtonDown -= OnGlobalMouseDown;
                GlobalMouse.Release();
            }
        };
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>True once the arming delay after opening has elapsed.</summary>
    private bool Armed => Environment.TickCount64 >= _armedAt;

    /// <summary>True while a modal child, a running command, or a close makes global input irrelevant.</summary>
    private bool Inert => _dismissing || _executing || _ignoreDeactivate || !IsVisible;

    /// <summary>
    /// Global (out-of-focus) keys. Escape always dismisses. A passive popup also dismisses on any
    /// other keypress — you were typing somewhere else, so it should get out of the way — until you
    /// click it, at which point the keyboard is yours (filtering, ↑/↓, Enter).
    /// </summary>
    private void OnGlobalKey(int msg, int vk, bool ctrl)
    {
        if (msg != GlobalKeys.WM_KEYDOWN && msg != GlobalKeys.WM_SYSKEYDOWN)
            return;

        if (vk == GlobalKeys.VK_ESCAPE)
        {
            Dispatcher.BeginInvoke(new Action(Dismiss));
            return;
        }

        if (_passive && !_engaged && Armed && !Inert && !IsModifier(vk))
            Dispatcher.BeginInvoke(new Action(Dismiss));
    }

    /// <summary>
    /// Modifiers don't count as "a keypress": they auto-repeat while held, so the Ctrl of the Ctrl+C
    /// that summoned the popup would otherwise close it immediately.
    /// </summary>
    private static bool IsModifier(int vk) =>
        vk is 0x10 or 0x11 or 0x12          // Shift / Ctrl / Alt (virtual)
           or >= 0xA0 and <= 0xA5           // L/R Shift, Ctrl, Alt
           or 0x5B or 0x5C                  // L/R Win
           or 0x14 or 0x90 or 0x91;         // CapsLock / NumLock / ScrollLock

    /// <summary>A passive popup closes on any mouse press that didn't land on it.</summary>
    private void OnGlobalMouseDown(int x, int y)
    {
        if (!Armed || Inert)
            return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var r)
            && x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom)
            return; // landed on the popup — that's a click for us, not a dismissal

        Dispatcher.BeginInvoke(new Action(Dismiss));
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (!_passive || _engaged)
            return;

        // The user clicked the popup, so it now legitimately holds focus: stop treating keystrokes as
        // "typing elsewhere" and put the caret in the filter box so ↑/↓/Enter work.
        _engaged = true;
        FilterBox.Focus();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Sticky focus: rather than closing when focus leaves, immediately rip it back so the next
        // click or keystroke still lands on the popup. Only Escape or running a command closes it.
        // Passive popups skip this entirely — they never take focus, so they never fight for it.
        if (!_passive)
            GuardFocus();
    }

    /// <summary>Reclaim the foreground unless a modal child is up, a command is running, or we're closing.</summary>
    private void GuardFocus()
    {
        if (_ignoreDeactivate || _dismissing || _executing || !IsVisible)
            return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && NativeMethods.GetForegroundWindow() != hwnd)
            ForceForeground();
    }

    /// <summary>
    /// Aggressively take the foreground. Plain <c>Activate()</c> loses to Windows' foreground lock
    /// when another (e.g. freshly launched) app holds it, so we re-assert topmost and attach to the
    /// current foreground thread's input queue — the standard trick to bypass the lock — before
    /// grabbing focus.
    /// </summary>
    private void ForceForeground()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
            return;

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        var foreThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var thisThread = NativeMethods.GetCurrentThreadId();
        var attached = foreThread != thisThread
            && NativeMethods.AttachThreadInput(foreThread, thisThread, true);
        try
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
            Activate();
        }
        finally
        {
            if (attached)
                NativeMethods.AttachThreadInput(foreThread, thisThread, false);
        }
        FilterBox.Focus();
    }

    /// <summary>Close the popup at most once (Escape, focus loss, and post-run all route here).</summary>
    private void Dismiss()
    {
        if (_dismissing)
            return;
        _dismissing = true;
        Close();
    }

    // ---- Script delete (the ✕ button on user scripts) ----
    private void DeleteScript_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: CommandItem item } || item.ScriptPath is not { } path)
            return;

        _ignoreDeactivate = true;
        try
        {
            var confirm = MessageBox.Show(
                $"Delete the script “{item.Display}”?\n\n{path}",
                "Clipboard Wizard", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                _items.Remove(item);
                SelectFirst();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't delete the script:\n{ex.Message}", "Clipboard Wizard",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _ignoreDeactivate = false;
            Activate();
            FilterBox.Focus();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T)
            d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

    // ---- Preview: show the current clipboard content above the command list ----
    private void BuildPreview(ClipboardPayload payload)
    {
        if (payload.HasImage)
        {
            PreviewImage.Source = payload.Image;
            ShowPreview(image: true);
            return;
        }

        var imageFile = payload.Files?.FirstOrDefault(ImageIO.IsImageFile);
        if (imageFile is not null)
        {
            try
            {
                PreviewImage.Source = ImageIO.Load(imageFile);
                ShowPreview(image: true);
                return;
            }
            catch
            {
                // Unreadable image — fall through to a text listing.
            }
        }

        if (payload.HasText)
        {
            PreviewText.Text = Truncate(payload.Text!, maxLines: 12, maxChars: 800);
            ShowPreview(image: false);
            return;
        }

        if (payload.HasFiles)
        {
            PreviewText.Text = string.Join('\n', payload.Files!.Select(Path.GetFileName));
            ShowPreview(image: false);
            return;
        }

        // Nothing on the clipboard (defensive — the trigger gate won't open an empty popup).
        PreviewText.Text = "<no clipboard>";
        ShowPreview(image: false);
    }

    private void ShowPreview(bool image)
    {
        PreviewBorder.Visibility = Visibility.Visible;
        PreviewImage.Visibility = image ? Visibility.Visible : Visibility.Collapsed;
        PreviewText.Visibility = image ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Truncate(string text, int maxLines, int maxChars)
    {
        var t = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var truncated = false;

        var lines = t.Split('\n');
        if (lines.Length > maxLines)
        {
            t = string.Join('\n', lines.Take(maxLines));
            truncated = true;
        }
        if (t.Length > maxChars)
        {
            t = t[..maxChars];
            truncated = true;
        }
        return truncated ? t.TrimEnd() + "\n…" : t;
    }

    private bool FilterPredicate(object obj)
    {
        var text = FilterBox?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        return obj is CommandItem item
            && item.Display.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Placement: position at the cursor in pixel space, clamped to the working area ----
    private void OnContentRendered(object? sender, EventArgs e)
    {
        NativeMethods.GetCursorPos(out var cursor);
        _anchor = new System.Drawing.Point(cursor.X, cursor.Y);
        PlaceAtAnchor();
    }

    /// <summary>
    /// Put the window next to the anchor (the cursor as it was when the popup opened) and clamp it to
    /// that monitor's working area. Re-run after the content changes height so it stays on screen.
    /// </summary>
    private void PlaceAtAnchor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
            return;

        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;

        var area = System.Windows.Forms.Screen.FromPoint(_anchor).WorkingArea;

        const int offset = 12;
        var x = _anchor.X + offset;
        var y = _anchor.Y + offset;

        // Flip to the other side of the cursor if we'd overflow, then hard-clamp to the screen.
        if (x + w > area.Right) x = _anchor.X - w - offset;
        if (y + h > area.Bottom) y = _anchor.Y - h - offset;
        x = Math.Max(area.Left, Math.Min(x, area.Right - w));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - h));

        // NOACTIVATE matters in passive mode: placement must never pull the foreground over.
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Swap in a payload that arrived just after the popup opened (the screenshot swap lands a moment
    /// after the copy) and rebuild the command list for it. Declined — leaving the popup as-is — once
    /// the user has started filtering or moving the selection, so nothing shifts under them.
    /// </summary>
    public bool TryUpdatePayload(ClipboardPayload payload, IReadOnlyList<IClipboardCommand> commands)
    {
        if (_dismissing || _executing || commands.Count == 0)
            return false;
        if (!string.IsNullOrEmpty(FilterBox.Text) || CommandList.SelectedIndex > 0)
            return false;

        _payload = payload;
        _items.Clear();
        for (var i = 0; i < commands.Count; i++)
            _items.Add(new CommandItem { Command = commands[i], Index = i });

        BuildPreview(payload);
        SelectFirst();
        // Height changes with the new list, so re-clamp once the new layout has been measured.
        Dispatcher.BeginInvoke(new Action(PlaceAtAnchor), DispatcherPriority.Loaded);
        return true;
    }

    // ---- Keyboard ----
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteSelected();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _view.Refresh();
        SelectFirst();
    }

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        // Ignore clicks that landed on the delete (✕) button — those run DeleteScript_Click.
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return;

        if (sender is ListBoxItem { DataContext: CommandItem item })
        {
            CommandList.SelectedItem = item;
            ExecuteSelected();
            e.Handled = true;
        }
    }

    private void MoveSelection(int delta)
    {
        var count = CommandList.Items.Count;
        if (count == 0)
            return;

        var next = CommandList.SelectedIndex + delta;
        next = Math.Max(0, Math.Min(next, count - 1));
        CommandList.SelectedIndex = next;
        CommandList.ScrollIntoView(CommandList.SelectedItem);
    }

    private void SelectFirst()
    {
        if (CommandList.Items.Count > 0)
            CommandList.SelectedIndex = 0;
    }

    private async void ExecuteSelected()
    {
        if (_executing || _dismissing || CommandList.SelectedItem is not CommandItem item)
            return;

        _executing = true;
        _ignoreDeactivate = true; // Hide() deactivates; don't let that auto-close us mid-run
        Hide(); // get the menu out of the way before the command runs

        SingleInstance.EnterBusy(); // mark this instance busy so a new launch asks before overriding
        try
        {
            await item.Command.ExecuteAsync(_payload, _context);
        }
        finally
        {
            SingleInstance.ExitBusy();
            Dismiss();
        }
    }
}
