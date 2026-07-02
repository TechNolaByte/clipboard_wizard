using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;

namespace ClipboardWizard.Commands;

/// <summary>
/// Runs a user Python script as an in-situ clipboard transform: the current clipboard text is
/// piped to the script's stdin, and whatever it writes to stdout replaces the clipboard.
/// </summary>
public sealed class PythonScriptCommand : IClipboardCommand
{
    private static readonly IReadOnlyDictionary<string, string> Utf8Env =
        new Dictionary<string, string> { ["PYTHONIOENCODING"] = "utf-8" };

    private readonly string _scriptPath;

    public PythonScriptCommand(string scriptPath)
    {
        _scriptPath = scriptPath;
        Name = Path.GetFileNameWithoutExtension(scriptPath);
    }

    public string Name { get; }

    public CommandCategory Category => CommandCategory.PythonScript;

    public bool CanExecute(ClipboardPayload payload) => payload.HasText;

    /// <summary>Run a Python script with <paramref name="input"/> on stdin; used here and by reformat.</summary>
    public static Task<ProcResult> RunScriptAsync(string scriptPath, string input) =>
        Proc.RunAsync("python", new[] { scriptPath }, input, env: Utf8Env);

    /// <summary>Build the process-log section shown in the .rtf audit log.</summary>
    public static string LogText(ProcResult r) =>
        $"exit code: {r.ExitCode}\n\nstdout:\n{r.StdOut}\n\nstderr:\n{r.StdErr}";

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        if (!payload.HasText)
            return;

        ProcResult result;
        try
        {
            result = await RunScriptAsync(_scriptPath, payload.Text!);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not run '{Name}':\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var command = $"Python script: {Name}";
        if (!result.Ok)
        {
            ActionLog.Write(command, _scriptPath, payload.Text, null, LogText(result), null, null);
            MessageBox.Show($"'{Name}' exited with code {result.ExitCode}:\n{result.StdErr}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        context.SuppressNextClipboardChange();
        ClipboardWriter.SetText(result.StdOut);
        ActionLog.Write(command, _scriptPath, payload.Text, null, LogText(result), result.StdOut, null);
    }
}
