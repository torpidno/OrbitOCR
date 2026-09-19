using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using OrbitOCR.Models;

namespace OrbitOCR.Services;

public class OcrService
{
    private global::Windows.Media.Ocr.OcrEngine? _engine;
    private string? _currentLanguageTag;

    public OcrService()
    {
        InitializeEngine(null);
    }

    public void InitializeEngine(string? languageTag)
    {
        _currentLanguageTag = languageTag;

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
            return new OcrExtractedResult
            {
                FullText = string.Empty,
                Lines = new List<string>(),
                WordCount = 0
            };
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

            var lines = new List<string>();
            var wordBoxes = new List<OcrWordBox>();
            var lineBoxes = new List<OcrLineBox>();
            var textBuilder = new StringBuilder();
            int wordCount = 0;

            foreach (var line in ocrResult.Lines)
            {
                var lineWords = new List<OcrWordBox>();
                double lineMinX = double.MaxValue, lineMinY = double.MaxValue;
                double lineMaxX = double.MinValue, lineMaxY = double.MinValue;

                foreach (var word in line.Words)
                {
                    var r = word.BoundingRect;
                    // Scale back down if 2x upscale was applied
                    var wordRect = new System.Windows.Rect(
                        r.X / scaleFactor,
                        r.Y / scaleFactor,
                        r.Width / scaleFactor,
                        r.Height / scaleFactor);

                    var wordBox = new OcrWordBox(word.Text, wordRect);
                    lineWords.Add(wordBox);
                    wordBoxes.Add(wordBox);

                    lineMinX = Math.Min(lineMinX, wordRect.X);
                    lineMinY = Math.Min(lineMinY, wordRect.Y);
                    lineMaxX = Math.Max(lineMaxX, wordRect.X + wordRect.Width);
                    lineMaxY = Math.Max(lineMaxY, wordRect.Y + wordRect.Height);
                }

                // Reconstruct line text preserving spacing between words
                var wordsInLine = line.Words.Select(w => w.Text);
                var formattedLine = string.Join(" ", wordsInLine).Trim();
                var textToUse = !string.IsNullOrWhiteSpace(formattedLine) ? formattedLine : line.Text;

                var lineRect = lineWords.Count > 0
                    ? new System.Windows.Rect(lineMinX, lineMinY, Math.Max(1, lineMaxX - lineMinX), Math.Max(1, lineMaxY - lineMinY))
                    : System.Windows.Rect.Empty;

                lineBoxes.Add(new OcrLineBox(textToUse, lineRect, lineWords));
                lines.Add(textToUse);
                textBuilder.AppendLine(textToUse);
                wordCount += line.Words.Count;
            }

            return new OcrExtractedResult
            {
                FullText = textBuilder.ToString().TrimEnd(),
                Lines = lines,
                Words = wordBoxes,
                LineBoxes = lineBoxes,
                WordCount = wordCount,
                TextAngle = ocrResult.TextAngle,
                LanguageTag = _engine.RecognizerLanguage.LanguageTag
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OcrService] RecognizeAsync error: {ex.Message}");
            return new OcrExtractedResult
            {
                FullText = string.Empty,
                Lines = new List<string>(),
                WordCount = 0
            };
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
