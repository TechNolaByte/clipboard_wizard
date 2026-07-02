using System.Collections.Generic;
using System.IO;
using ClipboardWizard.Models;

namespace ClipboardWizard.Services;

/// <summary>
/// Clipboard Hawk: while active, the popup is suppressed and each copy is silently recorded onto a
/// stack (with a "hit" sound), the count surfaced on the tray. Flushing joins the stack back onto
/// the clipboard; cancelling discards it. App wires <see cref="Changed"/> to update the tray.
/// </summary>
public static class Hawk
{
    private static readonly List<string> _items = new();

    public static bool Active { get; private set; }
    public static int Count => _items.Count;

    /// <summary>Raised on start/capture/flush/cancel so the tray can refresh.</summary>
    public static Action? Changed;

    public static void Start()
    {
        _items.Clear();
        Active = true;
        Changed?.Invoke();
    }

    /// <summary>Record a clipboard change while active. Returns true when it was captured.</summary>
    public static bool Capture(ClipboardPayload payload)
    {
        if (!Active)
            return false;

        var text = payload.HasText
            ? payload.Text!
            : (payload.HasFiles ? string.Join(Environment.NewLine, payload.Files!) : null);
        if (text is null)
            return false;

        _items.Add(text);
        PlayHit();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Join the stack back onto the clipboard and stop. No-op if empty stack still stops.</summary>
    public static void Flush()
    {
        if (!Active)
            return;

        var joined = string.Join(Environment.NewLine + Environment.NewLine, _items);
        var had = _items.Count;
        _items.Clear();
        Active = false;
        Changed?.Invoke();

        if (had > 0)
        {
            AppState.SuppressNextClipboardChange?.Invoke();
            ClipboardWriter.SetText(joined);
        }
    }

    public static void Cancel()
    {
        _items.Clear();
        Active = false;
        Changed?.Invoke();
    }

    private static void PlayHit()
    {
        try
        {
            var wav = Path.Combine(AppContext.BaseDirectory, "assets", "hit.wav");
            if (File.Exists(wav))
                new System.Media.SoundPlayer(wav).Play();
        }
        catch
        {
            // sound is optional
        }
    }
}
