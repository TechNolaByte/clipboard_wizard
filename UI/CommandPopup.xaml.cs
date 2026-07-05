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
    private readonly ClipboardPayload _payload;
    private readonly CommandContext _context;
    private readonly ObservableCollection<CommandItem> _items;
    private readonly ICollectionView _view;
    private bool _dismissing;
    private bool _executing;
    private bool _ignoreDeactivate;

    public CommandPopup(ClipboardPayload payload, IReadOnlyList<IClipboardCommand> commands, CommandContext context)
    {
        InitializeComponent();

        _payload = payload;
        _context = context;

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

        Loaded += (_, _) => FilterBox.Focus();
        ContentRendered += OnContentRendered;
        // Set the guard the instant any close begins (Escape, focus loss, or an external
        // Close() from App), so the Deactivated that a close raises can't re-enter Close().
        Closing += (_, _) => _dismissing = true;
        Deactivated += OnDeactivated; // dismiss when focus leaves the popup
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Sticky focus: rather than closing when focus leaves, grab it straight back so the next
        // click or keystroke still lands on the popup. Only Escape or running a command closes it.
        // Skipped while a modal child (delete confirm) is up, while a command runs, or during close.
        if (_ignoreDeactivate || _dismissing || _executing)
            return;

        // We just lost the foreground, so Windows still lets us reclaim it here.
        Activate();
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
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return;

        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;

        NativeMethods.GetCursorPos(out var cursor);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        var area = screen.WorkingArea;

        const int offset = 12;
        var x = cursor.X + offset;
        var y = cursor.Y + offset;

        // Flip to the other side of the cursor if we'd overflow, then hard-clamp to the screen.
        if (x + w > area.Right) x = cursor.X - w - offset;
        if (y + h > area.Bottom) y = cursor.Y - h - offset;
        x = Math.Max(area.Left, Math.Min(x, area.Right - w));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - h));

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
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
