using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ClipboardWizard.Services;

/// <summary>
/// Verbose mode: run a command in a visible terminal so the user can see everything. Observational
/// only — it does not capture output or apply a result to the clipboard. Implemented by writing a
/// small wrapper script (so arbitrary args/stdin need no command-line escaping) and opening it.
/// </summary>
public static class VerboseRunner
{
    public static void Run(string title, string exe, IEnumerable<string> args, string? stdinText)
    {
        var wrapper = Path.Combine(AppPaths.ScratchpadDir, $"verbose_{Guid.NewGuid():N}.ps1");

        var quotedArgs = args.Select(a => "'" + a.Replace("'", "''") + "'");
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference='Continue'");
        sb.AppendLine($"Write-Host '=== {title.Replace("'", "''")} ===' -ForegroundColor Cyan");
        sb.Append("$exe = '").Append(exe.Replace("'", "''")).AppendLine("'");
        sb.Append("$cmdArgs = @(").Append(string.Join(",", quotedArgs)).AppendLine(")");

        if (stdinText is not null)
        {
            sb.AppendLine("$in = @'");
            sb.AppendLine(stdinText);
            sb.AppendLine("'@");
            sb.AppendLine("$in | & $exe @cmdArgs");
        }
        else
        {
            sb.AppendLine("& $exe @cmdArgs");
        }

        sb.AppendLine("Write-Host \"`n=== done (exit $LASTEXITCODE) — result was NOT applied to the clipboard (verbose mode) ===\" -ForegroundColor Cyan");

        File.WriteAllText(wrapper, sb.ToString(), new UTF8Encoding(false));
        Terminal.RunScript(wrapper);
    }
}
