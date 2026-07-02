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
/// self-contained stdin→stdout Python script, save it into the scripts folder (so it becomes a
/// reusable command next time), then run it on the current clipboard text.
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

    public bool CanExecute(ClipboardPayload payload) => payload.HasText;

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        if (!payload.HasText)
            return;

        var spec = Prompts.AskText("Reformat — Python script",
            "Describe the transformation. Claude will write a reusable Python script, save it to " +
            "your scripts folder, and run it now.");
        if (string.IsNullOrWhiteSpace(spec))
            return;

        ClaudeResult result;
        try
        {
            result = await ClaudeCli.RunTextAsync(spec, null, SystemPrompt);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't run claude:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var script = StripCodeFences(result.Output);
        if (!result.Success || string.IsNullOrWhiteSpace(script))
        {
            MessageBox.Show($"Script generation failed:\n{result.FailureMessage}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string scriptPath;
        try
        {
            Directory.CreateDirectory(_scriptsDir);
            var unique = Guid.NewGuid().ToString("N")[..8];
            scriptPath = Path.Combine(_scriptsDir, $"{Slug(spec)}_{unique}.py");
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't save the script:\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Reuse the existing Python runner: pipes clipboard text in, replaces it with stdout.
        await new PythonScriptCommand(scriptPath).ExecuteAsync(payload, context);
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
