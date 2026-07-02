using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ClipboardWizard.Interop;

namespace ClipboardWizard.UI;

/// <summary>
/// A small, non-activating status chip shown near the mouse cursor while a command runs
/// (e.g. "Script foo running…", "Reformat — Claude processing…"). Topmost, never steals focus.
/// </summary>
public sealed class StatusToast : Window
{
    private static StatusToast? _current;
    private readonly TextBlock _text;

    private StatusToast(string message)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;

        _text = new TextBlock { Text = message, Foreground = Brush("#E5E7EB"), FontSize = 12.5 };
        Content = new Border
        {
            Background = Brush("#1E1F26"),
            BorderBrush = Brush("#3D59A1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = _text,
        };
        ContentRendered += (_, _) => PlaceAtCursor();
    }

    public static void Show(string message)
    {
        var app = System.Windows.Application.Current;
        app?.Dispatcher.Invoke(() =>
        {
            if (_current is null)
            {
                _current = new StatusToast(message);
                _current.Closed += (_, _) => _current = null;
                _current.Show();
            }
            else
            {
                _current._text.Text = message;
            }
        });
    }

    public static new void Hide()
    {
        var app = System.Windows.Application.Current;
        app?.Dispatcher.Invoke(() =>
        {
            _current?.Close();
            _current = null;
        });
    }

    private void PlaceAtCursor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return;

        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;

        NativeMethods.GetCursorPos(out var cursor);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        var area = screen.WorkingArea;

        var x = cursor.X + 16;
        var y = cursor.Y + 20;
        if (x + w > area.Right) x = area.Right - w - 4;
        if (y + h > area.Bottom) y = cursor.Y - h - 8;

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }
}
