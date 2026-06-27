using System.Diagnostics;
using System.IO;
using System.Text;
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
    private readonly string _scriptPath;

    public PythonScriptCommand(string scriptPath)
    {
        _scriptPath = scriptPath;
        Name = Path.GetFileNameWithoutExtension(scriptPath);
    }

    public string Name { get; }

    public CommandCategory Category => CommandCategory.PythonScript;

    public bool CanExecute(ClipboardPayload payload) => payload.HasText;

    public async Task ExecuteAsync(ClipboardPayload payload, CommandContext context)
    {
        if (!payload.HasText)
            return;

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(_scriptPath);
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

        string output;
        string error;
        int exitCode;
        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start python.");

            await proc.StandardInput.WriteAsync(payload.Text);
            proc.StandardInput.Close();

            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var errorTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            output = await outputTask;
            error = await errorTask;
            exitCode = proc.ExitCode;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not run '{Name}':\n{ex.Message}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (exitCode != 0)
        {
            MessageBox.Show($"'{Name}' exited with code {exitCode}:\n{error}", "Clipboard Wizard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        context.SuppressNextClipboardChange();
        ClipboardWriter.SetText(output);
    }
}
