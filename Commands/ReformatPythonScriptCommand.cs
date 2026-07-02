using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ClipboardWizard.Models;
using ClipboardWizard.Services;
using ClipboardWizard.UI;

namespace ClipboardWizard.Commands;

/// <summary>
/// "Reformat in situ — Python script": ask for a reformat instruction, have Sonnet WRITE a
/// self-contained stdin→stdout Python script, save it into the scripts folder (reusable next time)
/// plus a timestamped copy in the logs folder, then run it on the current clipboard content.
/// </summary>
public sealed class ReformatPythonScriptCommand : IClipboardCommand
{
    private const string SystemPrompt =
        "Output ONLY a complete, self-contained Python 3 script and nothing else — no prose, no " +
        "markdown fences. The script MUST read all of standard input as text via sys.stdin.read() " +
        "and write the transformed result to standard output via sys.stdout.write(...). Implement " +
        "exactly the transformation the user describes.";

    private readonly string _scriptsDir;

    public ReformatPythonScriptCommand(string scriptsDir) => _scriptsDir = scriptsDir;

    public string Name => "Reformat in situ — Python script (saved)";

    public CommandCategory Category => CommandCategory.Action;

    public bool CanExecute(ClipboardPayload payload) => payload.HasInput;

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        var input = payload.PrimaryText;
        if (input is null)
            return;

        var spec = Prompts.AskText("Reformat — Python script",
            "Describe the transformation. Claude will write a reusable Python script, save it, and run it now.",
            context: ReformatLlmCommand.Preview(payload, input));
        if (string.IsNullOrWhiteSpace(spec))
            return;

        if (AppState.Verbose)
        {
            VerboseRunner.Run("Reformat — Python script (generation)",
                ClaudeCli.Executable, ClaudeCli.TextArgs(spec, SystemPrompt), null);
            return;
        }

        ClaudeResult gen;
        StatusToast.Show("Reformat — writing Python script…");
        try
        {
            gen = await ClaudeCli.RunTextAsync(spec, null, SystemPrompt);
        }
        catch (Exception ex)
        {
            StatusToast.Hide();
            MessageBox.Show($"Couldn't run claude:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var script = StripCodeFences(gen.Output);
        if (!gen.Success || string.IsNullOrWhiteSpace(script))
        {
            StatusToast.Hide();
            MessageBox.Show($"Script generation failed:\n{gen.FailureMessage}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string scriptPath;
        try
        {
            Directory.CreateDirectory(_scriptsDir);
            var unique = Guid.NewGuid().ToString("N")[..8];
            var fileName = $"{Slug(spec)}_{unique}.py";
            scriptPath = Path.Combine(_scriptsDir, fileName);
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(scriptPath, script, utf8);

            // Also drop a timestamped copy in the logs folder for a permanent record.
            var stamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
            File.WriteAllText(Path.Combine(AppPaths.LogsDir, $"{stamp}_{fileName}"), script, utf8);
        }
        catch (Exception ex)
        {
            StatusToast.Hide();
            MessageBox.Show($"Couldn't save the script:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ProcResult run;
        StatusToast.Show("Reformat — running the new script…");
        try
        {
            run = await PythonScriptCommand.RunScriptAsync(scriptPath, input);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Saved the script but couldn't run it:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            StatusToast.Hide();
        }

        var processLog =
            $"Saved script: {scriptPath}\n\n--- generated script ---\n{script}\n\n" +
            $"--- run ---\n{PythonScriptCommand.LogText(run)}";

        if (!run.Ok)
        {
            ActionLog.Write("Reformat — Python script", spec, input, null, processLog, null, null);
            MessageBox.Show($"The generated script exited with code {run.ExitCode}:\n{run.StdErr}",
                "Clipboard Wizard", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        context.SuppressNextClipboardChange();
        ClipboardWriter.SetText(run.StdOut);
        ActionLog.Write("Reformat — Python script", spec, input, null, processLog, run.StdOut, null);
    }

    private static string StripCodeFences(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```"))
            return t;

        var lines = t.Split('\n').ToList();
        lines.RemoveAt(0); // opening ``` or ```python
        if (lines.Count > 0 && lines[^1].TrimEnd().EndsWith("```"))
            lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines).Trim();
    }

    private static string Slug(string spec)
    {
        var sb = new StringBuilder();
        foreach (var ch in spec.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
            if (sb.Length >= 24) break;
        }
        var s = sb.ToString().Trim('_');
        return s.Length == 0 ? "reformat" : s;
    }
}
