using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClipboardWizard.Services;

/// <summary>
/// Uploads a local image to an anonymous, temporary public host and returns a direct image URL. Used
/// by reverse-image "Search online" — Google Lens / Perplexity need the image at a URL, but a clipboard
/// image is local, so we hand them a short-lived hosted copy.
///
/// The host is <c>tmpfiles.org</c>: no account, no API key, and files auto-delete after ~1 hour, so
/// nothing lingers. Because this puts the clipboard image on a public URL, callers confirm with the
/// user before calling in.
/// </summary>
public static class ImageHost
{
    private const string UploadUrl = "https://tmpfiles.org/api/v1/upload";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>POST the file at <paramref name="path"/> to the host; returns a direct image URL.</summary>
    public static async Task<string> UploadAsync(string path, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(path, ct);
        form.Add(new ByteArrayContent(bytes), "file", Path.GetFileName(path));

        using var req = new HttpRequestMessage(HttpMethod.Post, UploadUrl) { Content = form };
        req.Headers.UserAgent.ParseAdd("ClipboardWizard/1.0");

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Image host returned {(int)resp.StatusCode} {resp.StatusCode}: {body.Trim()}");

        // Response: {"status":"success","data":{"url":"https://tmpfiles.org/<id>/<name>"}}
        string? pageUrl;
        try
        {
            pageUrl = JsonDocument.Parse(body).RootElement
                .GetProperty("data").GetProperty("url").GetString();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unexpected image-host response: {body.Trim()}", ex);
        }
        if (string.IsNullOrEmpty(pageUrl))
            throw new InvalidOperationException($"Image host returned no URL: {body.Trim()}");

        // The returned URL is a viewer page; the raw image lives at the same path with "/dl/" inserted
        // (https://tmpfiles.org/<id>/<name> -> https://tmpfiles.org/dl/<id>/<name>). Lens needs the raw URL.
        return pageUrl.Replace("tmpfiles.org/", "tmpfiles.org/dl/");
    }
}
