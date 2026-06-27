using System.Windows.Interop;
using ClipboardWizard.Interop;

namespace ClipboardWizard.Services;

/// <summary>
/// Watches the system clipboard via a message-only window and raises <see cref="ClipboardChanged"/>
/// on every genuine external change. Changes we make ourselves can be masked with
/// <see cref="SuppressNext"/> so features like Cycle Clipboard / Clipboard Hawk don't loop.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    // Parent handle that turns a window into a message-only window (no UI, just a message pump).
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private HwndSource? _source;
    private uint _lastSequence;
    private int _suppressCount;

    public event EventHandler? ClipboardChanged;

    public void Start()
    {
        var parameters = new HwndSourceParameters("ClipboardWizard.Listener")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        if (!NativeMethods.AddClipboardFormatListener(_source.Handle))
            throw new InvalidOperationException("AddClipboardFormatListener failed.");

        _lastSequence = NativeMethods.GetClipboardSequenceNumber();
    }

    /// <summary>
    /// Mask the next clipboard change (the one caused by our own write). Call this right
    /// before setting the clipboard from a command.
    /// </summary>
    public void SuppressNext() => Interlocked.Increment(ref _suppressCount);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_CLIPBOARDUPDATE)
            return IntPtr.Zero;

        handled = true;

        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == _lastSequence)
            return IntPtr.Zero; // duplicate notification for the same content
        _lastSequence = sequence;

        // If a command just wrote to the clipboard, swallow this one event.
        if (Interlocked.CompareExchange(ref _suppressCount, 0, 0) > 0)
        {
            Interlocked.Decrement(ref _suppressCount);
            return IntPtr.Zero;
        }

        ClipboardChanged?.Invoke(this, EventArgs.Empty);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null)
            return;

        NativeMethods.RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
