using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClipboardWizard.Commands;
using ClipboardWizard.Models;

namespace ClipboardWizard.Services;

/// <summary>
/// Produces the list of commands shown in the popup: the user's Python scripts (newest first),
/// then image operations (when the clipboard holds an image), then the built-in actions.
/// This is the single place to register new commands. Each command self-gates via
/// <see cref="IClipboardCommand.CanExecute"/>, so ordering here is purely about display grouping.
/// </summary>
public sealed class CommandRegistry
{
    private readonly string _scriptsDir;

    public CommandRegistry(string scriptsDir)
    {
        _scriptsDir = scriptsDir;
        EnsureScriptsFolder();
    }

    public string ScriptsDirectory => _scriptsDir;

    /// <summary>Returns the commands applicable to <paramref name="payload"/>, in display order.</summary>
    public IReadOnlyList<IClipboardCommand> GetCommands(ClipboardPayload payload)
    {
        var commands = new List<IClipboardCommand>();
        commands.AddRange(GetPythonScripts());
        commands.AddRange(GetImageCommands());
        commands.AddRange(GetActions());
        return commands.Where(c => c.CanExecute(payload)).ToList();
    }

    private IEnumerable<IClipboardCommand> GetPythonScripts()
    {
        if (!Directory.Exists(_scriptsDir))
            return Enumerable.Empty<IClipboardCommand>();

        return new DirectoryInfo(_scriptsDir)
            .EnumerateFiles("*.py")
            .OrderByDescending(f => f.LastWriteTimeUtc) // most recently edited on top
            .Select(f => (IClipboardCommand)new PythonScriptCommand(f.FullName))
            .ToList();
    }

    // Image operations. jpg->png is native (WPF codecs); GIF split/join use the bundled ffmpeg;
    // Transform uses ImageMagick if present else ffmpeg; Describe uses Sonnet vision via the CLI.
    private static IEnumerable<IClipboardCommand> GetImageCommands()
    {
        yield return new SplitGifCommand();
        yield return new JoinPngsToGifCommand();
        yield return new JpgToPngCommand();
        yield return new TransformImageCommand();
        yield return new DescribeImageCommand(DescribeMode.Title);
        yield return new DescribeImageCommand(DescribeMode.Verbose);
    }

    private IEnumerable<IClipboardCommand> GetActions()
    {
        // Implemented today:
        yield return new PowerShellCommand();
        yield return new ReformatLlmCommand();
        yield return new ReformatPythonScriptCommand(_scriptsDir);
        yield return new ActWithCommand();
        yield return new SendToPeersCommand();

        // On the roadmap — visible stubs so the menu reflects the full plan:
        yield return new NotImplementedCommand("Log to Obsidian daily journal");
        yield return new NotImplementedCommand("Clipboard Hawk (record stack)");
        yield return new NotImplementedCommand("Cycle Clipboard (fragment + paste)");
        yield return new NotImplementedCommand("Auto-format and print");
    }

    private void EnsureScriptsFolder()
    {
        Directory.CreateDirectory(_scriptsDir);

        // Seed a couple of examples on first run so the Scripts group isn't empty and the
        // expected stdin -> stdout contract is obvious.
        if (Directory.EnumerateFiles(_scriptsDir, "*.py").Any())
            return;

        File.WriteAllText(Path.Combine(_scriptsDir, "uppercase.py"),
            "import sys\n" +
            "data = sys.stdin.read()\n" +
            "sys.stdout.write(data.upper())\n");

        File.WriteAllText(Path.Combine(_scriptsDir, "collapse_whitespace.py"),
            "import sys, re\n" +
            "data = sys.stdin.read()\n" +
            "sys.stdout.write(re.sub(r'\\s+', ' ', data).strip())\n");
    }
}
