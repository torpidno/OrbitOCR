using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace OrbitOCR.Services;

/// <summary>
/// Sends a captured image straight into Google Lens and lands on the results page.
/// <para>
/// It does this by writing a tiny self-submitting page and opening it in the default
/// browser: the page posts the image as a multipart form to the same endpoint the Lens
/// web UI uses, then the browser follows the redirect to the results. Using the real
/// browser means Google's cookies/consent state are reused and no clipboard paste is needed.
/// </para>
/// </summary>
public static class LensSearchService
{
    // Exactly the form the Lens web UI submits (see google's own lens.google.com/v3/upload usage).
    internal const string UploadAction = "https://lens.google.com/v3/upload?ep=ccm&s=&st=";

    /// <summary>PNG-encodes the bitmap into the bytes embedded in the payload page.</summary>
    public static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// Builds the self-submitting HTML page. Pure so the upload contract can be asserted
    /// in a test without a browser or network.
    /// </summary>
    public static string BuildPayloadHtml(byte[] imageBytes, long timestamp)
    {
        // Base64's alphabet cannot contain '<', so it is safe to inline inside the script tag.
        string base64 = Convert.ToBase64String(imageBytes);

        return $$"""
<!doctype html>
<html>
<head><meta charset="utf-8"><title>Searching with Google Lens…</title></head>
<body>
<p>Opening Google Lens…</p>
<script>
(() => {
  const action = "{{UploadAction}}" + {{timestamp}};
  const bytes = Uint8Array.from(atob("{{base64}}"), c => c.charCodeAt(0));
  const file = new File([bytes], "image.png", { type: "image/png" });
  const transfer = new DataTransfer();
  transfer.items.add(file);

  const form = document.createElement("form");
  form.action = action;
  form.method = "POST";
  form.enctype = "multipart/form-data";

  const image = document.createElement("input");
  image.type = "file";
  image.name = "encoded_image";
  image.files = transfer.files;
  form.appendChild(image);

  document.body.appendChild(form);
  form.submit();
})();
</script>
</body>
</html>
""";
    }

    /// Time a payload page is kept around: long enough for a cold browser to read and submit
    /// it, short enough that the screenshot does not linger on disk.
    private static readonly TimeSpan PayloadLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Writes the payload page, opens it in the default browser, and schedules its deletion.
    /// </summary>
    public static string LaunchSearch(Bitmap bitmap)
    {
        string html = BuildPayloadHtml(EncodePng(bitmap), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        string directory = Path.Combine(Path.GetTempPath(), "OrbitOCR");
        Directory.CreateDirectory(directory);
        SweepStalePayloads(directory);

        // Unique per search: a second search must never overwrite a page the first browser
        // is still loading, and nobody can pre-create a predictable path.
        string path = Path.Combine(directory, $"lens_payload_{Guid.NewGuid():N}.html");
        File.WriteAllText(path, html);

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        _ = Task.Delay(PayloadLifetime).ContinueWith(_ => TryDelete(path));
        return path;
    }

    /// <summary>Removes payloads left behind by a previous crash, leaving recent ones alone.</summary>
    private static void SweepStalePayloads(string directory)
    {
        DateTime cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);

        foreach (string stale in Directory.EnumerateFiles(directory, "lens_payload_*.html"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(stale) < cutoff) File.Delete(stale);
            }
            catch
            {
                // Best effort - a locked file is cleaned on a later run.
            }
        }
    }

    /// <summary>Deletes a payload page, ignoring the usual "file in use / already gone" races.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort only - the startup sweep catches anything left behind.
        }
    }
}
