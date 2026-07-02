using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace ClipboardWizard.Services;

/// <summary>
/// Writes a per-action audit log as an <c>.rtf</c> file in <see cref="AppPaths.LogsDir"/>. Each log
/// records, for one clipboard action: the original clipboard data, the instruction, the process log
/// (commands run, claude/tool output), and the final version. Images are embedded inline.
///
/// Logging never throws — a failure here must not break the command.
/// </summary>
public static class ActionLog
{
    public static void Write(
        string command,
        string? instruction,
        string? originalText,
        string? originalImagePath,
        string? processLog,
        string? finalText,
        string? finalImagePath)
    {
        try
        {
            var body = new StringBuilder();
            Header(body, "Command", command);
            Header(body, "Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            body.Append("\\par\n");

            Section(body, "Instruction", instruction, null);
            Section(body, "Original", originalText, originalImagePath);
            Section(body, "Process log", processLog, null);
            Section(body, "Final", finalText, finalImagePath);

            var rtf = "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0 Consolas;}}\\f0\\fs20\n"
                      + body + "}";

            File.WriteAllText(UniquePath(), rtf, new UTF8Encoding(false));
        }
        catch
        {
            // Auditing is best-effort; never surface a logging failure to the user.
        }
    }

    private static void Header(StringBuilder sb, string label, string value)
    {
        sb.Append("{\\b ").Append(Escape(label)).Append(":} ").Append(Escape(value)).Append("\\par\n");
    }

    private static void Section(StringBuilder sb, string title, string? text, string? imagePath)
    {
        sb.Append("{\\b ").Append(Escape(title)).Append(":}\\par\n");

        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            sb.Append(ImageToRtf(imagePath)).Append("\\par\n");

        if (!string.IsNullOrEmpty(text))
            sb.Append(Escape(text)).Append("\\par\n");

        if (string.IsNullOrEmpty(imagePath) && string.IsNullOrEmpty(text))
            sb.Append(Escape("(none)")).Append("\\par\n");

        sb.Append("\\par\n");
    }

    private static string UniquePath()
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
        var path = Path.Combine(AppPaths.LogsDir, stamp + ".rtf");
        var i = 1;
        while (File.Exists(path))
            path = Path.Combine(AppPaths.LogsDir, $"{stamp}_{i++}.rtf");
        return path;
    }

    private static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        var sb = new StringBuilder(s.Length + 16);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '{': sb.Append("\\{"); break;
                case '}': sb.Append("\\}"); break;
                case '\r': break;
                case '\n': sb.Append("\\par\n"); break;
                case '\t': sb.Append("\\tab "); break;
                default:
                    if (ch < 0x80)
                        sb.Append(ch);
                    else
                        sb.Append("\\u").Append((int)(short)ch).Append('?'); // signed 16-bit unit
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Re-encode any image to PNG and embed it as an RTF \pngblip.</summary>
    private static string ImageToRtf(string path)
    {
        try
        {
            var src = ImageIO.Load(path);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            var hex = Convert.ToHexString(ms.ToArray()).ToLowerInvariant();

            var hmW = (int)Math.Round(src.PixelWidth / 96.0 * 2540);  // himetric (0.01mm)
            var hmH = (int)Math.Round(src.PixelHeight / 96.0 * 2540);
            var twW = src.PixelWidth * 15;   // twips (96 dpi → 15 twips/px)
            var twH = src.PixelHeight * 15;

            var sb = new StringBuilder(hex.Length + 128);
            sb.Append("{\\pict\\pngblip\\picw").Append(hmW).Append("\\pich").Append(hmH)
              .Append("\\picwgoal").Append(twW).Append("\\pichgoal").Append(twH).Append('\n');
            for (var i = 0; i < hex.Length; i += 128)
                sb.Append(hex, i, Math.Min(128, hex.Length - i)).Append('\n');
            sb.Append('}');
            return sb.ToString();
        }
        catch
        {
            return Escape($"[image: {path}]");
        }
    }
}
