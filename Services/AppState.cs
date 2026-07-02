namespace ClipboardWizard.Services;

/// <summary>Process-wide runtime toggles (set from the tray menu).</summary>
public static class AppState
{
    /// <summary>
    /// When on, script/LLM commands open in a visible terminal for observation instead of running
    /// headless. In verbose mode the result is shown live but not auto-applied to the clipboard.
    /// </summary>
    public static bool Verbose { get; set; }
}
