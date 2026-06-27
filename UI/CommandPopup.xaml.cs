using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using ClipboardWizard.Interop;
using ClipboardWizard.Models;

namespace ClipboardWizard.UI;

/// <summary>
/// The command menu that appears at the cursor when the clipboard changes. Owns its placement
/// (pixel-accurate, screen-clamped), filtering, and keyboard handling.
/// </summary>
public partial class CommandPopup : Window
{
    private readonly ClipboardPayload _payload;
    private readonly CommandContext _context;
    private readonly ICollectionView _view;
    private bool _isClosing;

    public CommandPopup(ClipboardPayload payload, IReadOnlyList<IClipboardCommand> commands, CommandContext context)
    {
        InitializeComponent();

        _payload = payload;
        _context = context;

        var items = commands
            .Select((c, i) => new CommandItem { Command = c, Index = i })
            .ToList();

        _view = CollectionViewSource.GetDefaultView(items);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CommandItem.Group)));
        // Group order first, then original index — keeps "newest script on top" intact.
        _view.SortDescriptions.Add(new SortDescription(nameof(CommandItem.GroupOrder), ListSortDirection.Ascending));
        _view.SortDescriptions.Add(new SortDescription(nameof(CommandItem.Index), ListSortDirection.Ascending));
        _view.Filter = FilterPredicate;
        CommandList.ItemsSource = _view;

        SelectFirst();

        Loaded += (_, _) => FilterBox.Focus();
        ContentRendered += OnContentRendered;
        Deactivated += (_, _) => Close(); // dismiss when focus leaves the popup
        PreviewKeyDown += OnPreviewKeyDown;
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
                Close();
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
        if (sender is System.Windows.Controls.ListBoxItem { DataContext: CommandItem item })
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
        if (_isClosing || CommandList.SelectedItem is not CommandItem item)
            return;

        _isClosing = true;
        Hide(); // get the menu out of the way before the command runs
        await item.Command.ExecuteAsync(_payload, _context);
        Close();
    }
}
