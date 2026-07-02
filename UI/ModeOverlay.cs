using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardWizard.Interop;

namespace ClipboardWizard.UI;

/// <summary>
/// A small always-on-top status card pinned to the top-left of the primary screen while a mode
/// (Clipboard Hawk / Clipboard Cycle) is active. Shows the mode, a detail line, and an optional
/// thumbnail. Non-activating, so it never steals focus.
/// </summary>
public sealed class ModeOverlay : Window
{
    private static ModeOverlay? _current;

    private readonly TextBlock _title;
    private readonly TextBlock _detail;
    private readonly Image _thumb;

    private ModeOverlay()
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

        _title = new TextBlock { Foreground = Brush("#E5E7EB"), FontSize = 12.5, FontWeight = FontWeights.SemiBold };
        _detail = new TextBlock { Foreground = Brush("#C8CDD6"), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
        _thumb = new Image { MaxHeight = 40, MaxWidth = 64, Margin = new Thickness(10, 0, 0, 0), Visibility = Visibility.Collapsed };

        var textCol = new StackPanel();
        textCol.Children.Add(_title);
        textCol.Children.Add(_detail);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(textCol);
        row.Children.Add(_thumb);

        Content = new Border
        {
            Background = Brush("#1E1F26"),
            BorderBrush = Brush("#3D59A1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Child = row,
        };

        ContentRendered += (_, _) => PlaceTopLeft();
    }

    public static void Update(string title, string detail, BitmapSource? thumbnail)
    {
        var app = System.Windows.Application.Current;
        app?.Dispatcher.Invoke(() =>
        {
            _current ??= Create();
            _current._title.Text = title;
            _current._detail.Text = detail;
            if (thumbnail is not null)
            {
                _current._thumb.Source = thumbnail;
                _current._thumb.Visibility = Visibility.Visible;
            }
            else
            {
                _current._thumb.Source = null;
                _current._thumb.Visibility = Visibility.Collapsed;
            }
            if (!_current.IsVisible)
                _current.Show();
            _current.PlaceTopLeft();
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

    private static ModeOverlay Create()
    {
        var o = new ModeOverlay();
        o.Closed += (_, _) => { if (ReferenceEquals(_current, o)) _current = null; };
        return o;
    }

    private void PlaceTopLeft()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, area.Left + 12, area.Top + 12, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }
}
