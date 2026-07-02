using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using ClipboardWizard.Models;

namespace ClipboardWizard.Services;

/// <summary>
/// Clipboard Hawk: while active, the popup is suppressed and each copy is recorded onto a stack
/// (with a hit sound); text/file copies join back onto the clipboard on flush, image copies are
/// saved to scratchpad (their path goes on the stack). Exposes the last item (preview text or
/// thumbnail) and count for the on-screen overlay. Ends on a global Escape (flush) via GlobalKeys.
/// </summary>
public static class Hawk
{
    private static readonly List<string> _items = new();

    public static bool Active { get; private set; }
    public static int Count => _items.Count;

    /// <summary>Short preview of the last captured item ("(image)" for images).</summary>
    public static string LastPreview { get; private set; } = "";

    /// <summary>Thumbnail of the last captured image, if the last item was an image.</summary>
    public static BitmapSource? LastThumbnail { get; private set; }

    public static Action? Changed;

    public static void Start()
    {
        _items.Clear();
        LastPreview = "";
        LastThumbnail = null;
        Active = true;
        GlobalKeys.Key += OnKey;
        GlobalKeys.Acquire();
        Changed?.Invoke();
    }

    /// <summary>Record a clipboard change while active. Returns true when captured.</summary>
    public static bool Capture(ClipboardPayload payload)
    {
        if (!Active)
            return false;

        if (payload.HasText)
        {
            Add(payload.Text!, Snippet(payload.Text!), null);
            return true;
        }
        if (payload.HasImage)
        {
            var path = ImageIO.SavePng(payload.Image!, AppPaths.ScratchpadDir);
            var thumb = payload.Image!;
            if (thumb.CanFreeze && !thumb.IsFrozen) thumb.Freeze();
            Add(path, "(image)", thumb);
            return true;
        }
        if (payload.HasFiles)
        {
            var t = string.Join(Environment.NewLine, payload.Files!);
            Add(t, Snippet(t), null);
            return true;
        }
        return false;
    }

    private static void Add(string stackText, string preview, BitmapSource? thumb)
    {
        _items.Add(stackText);
        LastPreview = preview;
        LastThumbnail = thumb;
        PlayHit();
        Changed?.Invoke();
    }

    /// <summary>End and join the recorded stack back onto the clipboard.</summary>
    public static void Flush()
    {
        if (!Active)
            return;
        var joined = string.Join(Environment.NewLine + Environment.NewLine, _items);
        var had = _items.Count;
        Stop();
        if (had > 0)
        {
            AppState.SuppressNextClipboardChange?.Invoke();
            ClipboardWriter.SetText(joined);
        }
    }

    /// <summary>End and discard the recorded stack.</summary>
    public static void Cancel() => Stop();

    private static void Stop()
    {
        if (!Active)
            return;
        Active = false;
        GlobalKeys.Key -= OnKey;
        GlobalKeys.Release();
        _items.Clear();
        LastThumbnail = null;
        LastPreview = "";
        Changed?.Invoke();
    }

    private static void OnKey(int msg, int vk, bool ctrl)
    {
        if ((msg == GlobalKeys.WM_KEYDOWN || msg == GlobalKeys.WM_SYSKEYDOWN) && vk == GlobalKeys.VK_ESCAPE)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(Flush));
    }

    internal static string Snippet(string s)
    {
        var one = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return one.Length <= 10 ? one : one[..10] + "…";
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
