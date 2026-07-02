using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClipboardWizard.UI;

/// <summary>
/// Small, code-only dialogs shared by the commands: a text-input prompt, a yes/no confirm, and a
/// scrollable result view. Styled to match the popup's dark theme. Ownerless and centered so they
/// behave even though the popup hides itself before a command runs.
/// </summary>
public static class Prompts
{
    private static readonly Brush Bg = Brush("#1E1F26");
    private static readonly Brush Panel = Brush("#2A2C36");
    private static readonly Brush Fg = Brush("#E5E7EB");
    private static readonly Brush Border = Brush("#3A3D48");
    private static readonly Brush Accent = Brush("#3D59A1");

    /// <summary>Prompt for a line (or paragraph) of text. Returns null if cancelled.</summary>
    public static string? AskText(string title, string message, string initial = "", bool multiline = false)
    {
        var win = NewWindow(title, multiline ? 200 : 150);

        var root = new DockPanel { Margin = new Thickness(14) };

        var label = new TextBlock
        {
            Text = message,
            Foreground = Fg,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 13,
        };
        DockPanel.SetDock(label, Dock.Top);

        var box = new TextBox
        {
            Text = initial,
            Background = Panel,
            Foreground = Fg,
            CaretBrush = Fg,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 13,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            MinHeight = multiline ? 80 : 0,
        };

        string? result = null;
        var buttons = ButtonRow(
            ("Cancel", () => win.Close()),
            ("OK", () => { result = box.Text; win.DialogResult = true; win.Close(); }));
        DockPanel.SetDock(buttons, Dock.Bottom);

        root.Children.Add(label);
        root.Children.Add(buttons);
        root.Children.Add(box); // fills remaining space
        win.Content = root;

        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && (!multiline || System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control))
            {
                result = box.Text;
                win.DialogResult = true;
                win.Close();
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                win.Close();
            }
        };

        win.ShowDialog();
        return win.DialogResult == true ? result : null;
    }

    /// <summary>Yes/No confirmation. Returns true only on Yes.</summary>
    public static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>Show a (potentially long) result with Copy + Close.</summary>
    public static void ShowResult(string title, string text)
    {
        var win = NewWindow(title, 360);
        win.Width = 520;

        var root = new DockPanel { Margin = new Thickness(14) };

        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            Background = Panel,
            Foreground = Fg,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var buttons = ButtonRow(
            ("Copy", () => { try { System.Windows.Clipboard.SetText(text); } catch { /* ignore */ } }),
            ("Close", () => win.Close()));
        DockPanel.SetDock(buttons, Dock.Bottom);

        root.Children.Add(buttons);
        root.Children.Add(box);
        win.Content = root;
        win.ShowDialog();
    }

    private static Window NewWindow(string title, double height) => new()
    {
        Title = title,
        Width = 440,
        Height = height,
        Background = Bg,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        ResizeMode = ResizeMode.CanResize,
        ShowInTaskbar = false,
        Topmost = true,
    };

    private static StackPanel ButtonRow(params (string Label, Action OnClick)[] items)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        foreach (var (label, onClick) in items)
        {
            var b = new Button
            {
                Content = label,
                MinWidth = 76,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 4, 10, 4),
                Background = label is "OK" or "Copy" ? Accent : Panel,
                Foreground = Fg,
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            b.Click += (_, _) => onClick();
            panel.Children.Add(b);
        }
        return panel;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
