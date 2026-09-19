using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using OrbitOCR.Models;

namespace OrbitOCR.Services;

public class OcrService
{
    private global::Windows.Media.Ocr.OcrEngine? _engine;

    public OcrService()
    {
        InitializeEngine(null);
    }

    public void InitializeEngine(string? languageTag)
    {
        if (!string.IsNullOrEmpty(languageTag))
        {
            try
            {
                var lang = new global::Windows.Globalization.Language(languageTag);
                if (global::Windows.Media.Ocr.OcrEngine.IsLanguageSupported(lang))
                {
                    _engine = global::Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(lang);
                    return;
                }
            }
            catch
            {
                // Fallback to user profile
            }
        }

        // Try user profile languages first
        _engine = global::Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();

        // Fallback to first available recognizer language
        if (_engine == null && global::Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Count > 0)
        {
            _engine = global::Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(
                global::Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages[0]);
        }
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        var list = new List<string>();
        foreach (var lang in global::Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages)
        {
            list.Add(lang.LanguageTag);
        }
        return list;
    }

    public async Task<OcrExtractedResult> RecognizeAsync(Bitmap bitmap)
    {
        if (_engine == null)
        {
            return new OcrExtractedResult();
        }

        // Apply 2x high-quality scaling for cropped snippets to significantly boost character recognition on small fonts
        bool shouldUpscale = bitmap.Width <= 1500 && bitmap.Height <= 1500;
        double scaleFactor = shouldUpscale ? 2.0 : 1.0;

        Bitmap bitmapToProcess;
        if (shouldUpscale)
        {
            int upW = bitmap.Width * 2;
            int upH = bitmap.Height * 2;
            bitmapToProcess = new Bitmap(upW, upH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmapToProcess))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.DrawImage(bitmap, 0, 0, upW, upH);
            }
        }
        else
        {
            bitmapToProcess = bitmap;
        }

        try
        {
            // Convert Bitmap to SoftwareBitmap via InMemoryRandomAccessStream
            using var ms = new MemoryStream();
            bitmapToProcess.Save(ms, ImageFormat.Png);
            byte[] bytes = ms.ToArray();

            using var ras = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new global::Windows.Storage.Streams.DataWriter(ras.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }

            var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

            var ocrResult = await _engine.RecognizeAsync(softwareBitmap);

            var words = new List<OcrWordBox>();
            var text = new StringBuilder();

            foreach (var line in ocrResult.Lines)
            {
                // Reconstruct line text preserving spacing between words
                var lineText = string.Join(" ", line.Words.Select(w => w.Text)).Trim();
                if (lineText.Length > 0)
                {
                    text.AppendLine(lineText);
                }

                foreach (var word in line.Words)
                {
                    var r = word.BoundingRect;
                    // Scale back down if 2x upscale was applied
                    words.Add(new OcrWordBox(word.Text, new System.Windows.Rect(
                        r.X / scaleFactor,
                        r.Y / scaleFactor,
                        r.Width / scaleFactor,
                        r.Height / scaleFactor)));
                }
            }

            return new OcrExtractedResult
            {
                FullText = text.ToString().TrimEnd(),
                Words = words
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OcrService] RecognizeAsync error: {ex.Message}");
            return new OcrExtractedResult();
        }
        finally
        {
            if (shouldUpscale)
            {
                bitmapToProcess.Dispose();
            }
        }
    }
}
