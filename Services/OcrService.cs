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

        try
        {
            // Convert Bitmap to SoftwareBitmap via InMemoryRandomAccessStream
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
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
            var textBuilder = new StringBuilder();
            int wordCount = 0;

            foreach (var line in ocrResult.Lines)
            {
                // Reconstruct line text preserving spacing between words
                var wordsInLine = line.Words.Select(w => w.Text);
                var formattedLine = string.Join(" ", wordsInLine).Trim();
                
                // If line.Text is already populated and clean, use it
                var textToUse = !string.IsNullOrWhiteSpace(formattedLine) ? formattedLine : line.Text;

                lines.Add(textToUse);
                textBuilder.AppendLine(textToUse);
                wordCount += line.Words.Count;
            }

            return new OcrExtractedResult
            {
                FullText = textBuilder.ToString().TrimEnd(),
                Lines = lines,
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
    }
}
