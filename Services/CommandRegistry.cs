using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClipboardWizard.Commands;
using ClipboardWizard.Models;

namespace ClipboardWizard.Services;

/// <summary>
/// Produces the list of commands shown in the popup: Research (Ask Claude / Search online), the user's
/// Python scripts (newest first), image operations (when the clipboard holds an image), the built-in
/// actions, and the Collect modes. This is the single place to register new commands. Each command
/// self-gates via <see cref="IClipboardCommand.CanExecute"/>, and the popup groups by category, so the
/// order of items within a category is the only thing registration order controls.
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
        commands.AddRange(GetResearch());
        commands.AddRange(GetPythonScripts());
        commands.AddRange(GetImageCommands());
        commands.AddRange(GetActions());
        commands.AddRange(GetCollect());
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
        yield return new DescribeImageCommand(DescribeMode.Transcribe);
        yield return new SplitGifCommand();
        yield return new JoinPngsToGifCommand();
        yield return new JpgToPngCommand();
        yield return new TransformImageCommand();
        yield return new DescribeImageCommand(DescribeMode.Title);
        yield return new DescribeImageCommand(DescribeMode.Verbose);
    }

    // Research group: ask an AI about the clipboard. "Ask Claude" is first so it's the popup's
    // default selection (Research sorts before every other group), then the browser searches.
    private static IEnumerable<IClipboardCommand> GetResearch()
    {
        yield return new AskClaudeCommand();
        yield return new SearchOnlineCommand(SearchEngine.Google);
        yield return new SearchOnlineCommand(SearchEngine.Perplexity);
    }

    private IEnumerable<IClipboardCommand> GetActions()
    {
        // Implemented today:
        yield return new PowerShellCommand();
        yield return new ReformatLlmCommand();
        yield return new ReformatPythonScriptCommand(_scriptsDir);
        yield return new ActWithCommand();
        yield return new SendToPeersCommand();
    }

    // Collect group: capture/collection modes.
    private static IEnumerable<IClipboardCommand> GetCollect()
    {
        yield return new NotImplementedCommand("Log to Obsidian daily journal", CommandCategory.Collect);
        yield return new HawkCommand();
        yield return new ClipboardCycleCommand();
        yield return new NotImplementedCommand("Auto-format and print", CommandCategory.Collect);
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

        // Ported from an earlier prototype (see docs/prior-art.md).
        File.WriteAllText(Path.Combine(_scriptsDir, "convert_path_slashes.py"),
            "import sys\n" +
            "# Convert Windows backslashes to Unix forward slashes.\n" +
            "sys.stdout.write(sys.stdin.read().replace('\\\\', '/'))\n");

        File.WriteAllText(Path.Combine(_scriptsDir, "search_clipboard_lines.py"),
            "import sys, webbrowser\n" +
            "# Open a browser tab for each non-empty line: a URL as-is, otherwise a web search.\n" +
            "# The clipboard is left unchanged (the input is echoed back to stdout).\n" +
            "text = sys.stdin.read()\n" +
            "for line in text.splitlines():\n" +
            "    line = line.strip()\n" +
            "    if line:\n" +
            "        url = line if '://' in line else 'https://www.google.com/search?q=' + line.replace(' ', '+')\n" +
            "        webbrowser.open_new_tab(url)\n" +
            "sys.stdout.write(text)\n");
    }
}
