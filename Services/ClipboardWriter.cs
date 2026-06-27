using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace ClipboardWizard.Services;

/// <summary>
/// Centralised clipboard writes with retry. Commands should go through here so there's a single
/// place that handles the clipboard being briefly locked by another process.
/// Must be called on the STA UI thread.
/// </summary>
public static class ClipboardWriter
{
    public static void SetText(string text)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
    }
}
