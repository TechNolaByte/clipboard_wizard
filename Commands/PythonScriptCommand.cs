using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

/// <summary>
/// Runs a user Python script as an in-situ clipboard transform: the current clipboard text (or, for
/// unrecognized files, their path(s)) is piped to the script's stdin, and its stdout replaces the
/// clipboard.
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

    /// <summary>Path to the .py file (used by the popup's delete button).</summary>
    public string ScriptPath => _scriptPath;

    public CommandCategory Category => CommandCategory.PythonScript;

    public bool CanExecute(ClipboardPayload payload) => payload.HasInput;

    public static Task<ProcResult> RunScriptAsync(string scriptPath, string input) =>
        Proc.RunAsync("python", new[] { scriptPath }, input, env: Utf8Env);

    public static string LogText(ProcResult r) =>
        $"exit code: {r.ExitCode}\n\nstdout:\n{r.StdOut}\n\nstderr:\n{r.StdErr}";

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var input = payload.PrimaryText;
        if (input is null)
            return;

        if (AppState.Verbose)
        {
            VerboseRunner.Run($"Script: {Name}", "python", new[] { _scriptPath }, input);
            return;
        }

        ProcResult result;
        StatusToast.Show($"Script “{Name}” running…");
        try
        {
            result = await RunScriptAsync(_scriptPath, input);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not run '{Name}':\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            StatusToast.Hide();
        }

        var command = $"Python script: {Name}";
        if (!result.Ok)
        {
            ActionLog.Write(command, _scriptPath, input, null, LogText(result), null, null);
            MessageBox.Show($"'{Name}' exited with code {result.ExitCode}:\n{result.StdErr}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        context.SuppressNextClipboardChange();
        ClipboardWriter.SetText(result.StdOut);
        ActionLog.Write(command, _scriptPath, input, null, LogText(result), result.StdOut, null);
    }
}
