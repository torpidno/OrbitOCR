using System.Drawing;
using OrbitOCR.Models;
using OrbitOCR.Services;

namespace OrbitOCR.Tests;

[TestClass]
public class OrbitOcrTests
{
    [TestMethod]
    public void TestCropBitmap_ValidRegion()
    {
        using var source = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Red);
        }

        var cropRect = new Rectangle(10, 10, 40, 40);
        using var cropped = ScreenCaptureService.CropBitmap(source, cropRect);

        Assert.IsNotNull(cropped);
        Assert.AreEqual(40, cropped.Width);
        Assert.AreEqual(40, cropped.Height);
    }

    [TestMethod]
    public void TestCropBitmap_ClampsOutOfBounds()
    {
        using var source = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Blue);
        }

        // Oversized rect
        var cropRect = new Rectangle(80, 80, 50, 50);
        using var cropped = ScreenCaptureService.CropBitmap(source, cropRect);

        Assert.IsNotNull(cropped);
        Assert.AreEqual(20, cropped.Width);
        Assert.AreEqual(20, cropped.Height);
    }

    [TestMethod]
    public void TestCropBitmap_ZeroOrNegativeSizeReturnsNull()
    {
        using var source = new Bitmap(100, 100);

        var emptyRect = new Rectangle(10, 10, 0, 0);
        var cropped = ScreenCaptureService.CropBitmap(source, emptyRect);
        Assert.IsNull(cropped);

        var outsideRect = new Rectangle(120, 120, 20, 20);
        var croppedOutside = ScreenCaptureService.CropBitmap(source, outsideRect);
        Assert.IsNull(croppedOutside);
    }

    [TestMethod]
    public void TestSettings_DefaultValues()
    {
        var settings = new AppSettings();

        Assert.IsTrue(settings.HotkeyCtrl);
        Assert.IsTrue(settings.HotkeyShift);
        Assert.IsFalse(settings.HotkeyAlt);
        Assert.IsFalse(settings.HotkeyWin);
        Assert.AreEqual("S", settings.HotkeyKey);
        Assert.IsTrue(settings.PlaySounds);
    }

    [TestMethod]
    public void TestFormatHotkeyString()
    {
        var settings = new AppSettings
        {
            HotkeyCtrl = true,
            HotkeyShift = true,
            HotkeyAlt = false,
            HotkeyWin = false,
            HotkeyKey = "S"
        };

        var str = App.FormatHotkeyString(settings);
        Assert.AreEqual("Ctrl+Shift+S", str);

        settings.HotkeyAlt = true;
        settings.HotkeyKey = "O";
        var str2 = App.FormatHotkeyString(settings);
        Assert.AreEqual("Ctrl+Shift+Alt+O", str2);
    }

    [TestMethod]
    public void TestOcrExtractedResult_HasTextLogic()
    {
        var empty = new OcrExtractedResult { FullText = "" };
        Assert.IsFalse(empty.HasText);

        var whitespace = new OcrExtractedResult { FullText = "   \n\t  " };
        Assert.IsFalse(whitespace.HasText);

        var withText = new OcrExtractedResult { FullText = "OrbitOCR Pixel Snip" };
        Assert.IsTrue(withText.HasText);
    }

    [TestMethod]
    public void TestOcrService_LanguagesDiscovery()
    {
        var ocr = new OcrService();
        var languages = ocr.GetAvailableLanguages();

        Assert.IsNotNull(languages);
        // On any standard Windows 10/11 system, at least one OCR language pack or user language is available
        Console.WriteLine($"Discovered {languages.Count} OCR languages: {string.Join(", ", languages)}");
    }

    [TestMethod]
    public void TestScreenCapture_GetVirtualScreenBounds()
    {
        var service = new ScreenCaptureService();
        var bounds = service.GetVirtualScreenBounds();

        Assert.IsTrue(bounds.Width > 0, "Virtual screen width must be positive");
        Assert.IsTrue(bounds.Height > 0, "Virtual screen height must be positive");
        Console.WriteLine($"Screen Virtual Bounds: {bounds.Width} x {bounds.Height}, MaxImageDimension: {Windows.Media.Ocr.OcrEngine.MaxImageDimension}");
    }

    [TestMethod]
    public async Task TestOcr_EndToEndTextRecognition()
    {
        // Render crisp text onto an image
        using var bmp = new Bitmap(400, 100);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Arial", 28, FontStyle.Bold);
            using var brush = new SolidBrush(Color.Black);
            g.DrawString("ORBIT OCR 2026", font, brush, new PointF(10, 25));
        }

        var ocr = new OcrService();
        var result = await ocr.RecognizeAsync(bmp);

        Console.WriteLine($"Extracted OCR text: '{result.FullText}', Words: {result.Words.Count}");
        Assert.IsTrue(result.HasText, "OCR should have extracted text from the rendered image");
        Assert.IsTrue(result.FullText.Contains("ORBIT", StringComparison.OrdinalIgnoreCase) ||
                      result.FullText.Contains("OCR", StringComparison.OrdinalIgnoreCase),
                      $"Expected 'ORBIT' or 'OCR' in '{result.FullText}'");
    }

    [TestMethod]
    public async Task TestOcr_SmallTextRecognition()
    {
        // Full HD desktop size with various UI text snippets in dark mode
        using var bmp = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(24, 24, 27)); // Dark mode
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var font = new Font("Segoe UI", 10, FontStyle.Regular);
            using var brush = new SolidBrush(Color.FromArgb(220, 220, 225));
            g.DrawString("File Edit View Git Project Build Debug Format Tools Window Help", font, brush, new PointF(20, 10));
            g.DrawString("Solution Explorer - Folder View", font, brush, new PointF(1500, 40));
            g.DrawString("Search (Ctrl+Q)", font, brush, new PointF(800, 10));
            g.DrawString("The quick brown fox jumps over the lazy dog", font, brush, new PointF(200, 500));
        }

        var ocr = new OcrService();
        var result = await ocr.RecognizeAsync(bmp);

        Console.WriteLine($"Full HD OCR: '{result.FullText}', Words: {result.Words.Count}");
        Assert.IsTrue(result.Words.Count >= 10, $"Expected >= 10 words, got {result.Words.Count}");
    }

    [TestMethod]
    public void TestLens_PayloadHtml_UsesLensUploadContract()
    {
        var png = new byte[] { 1, 2, 3, 4 };
        string html = LensSearchService.BuildPayloadHtml(png, 1234567890);

        // The contract Lens' own web form uses: multipart POST of encoded_image to /v3/upload.
        StringAssert.Contains(html, "https://lens.google.com/v3/upload?ep=ccm&s=&st=");
        StringAssert.Contains(html, "1234567890");
        StringAssert.Contains(html, "encoded_image");
        StringAssert.Contains(html, "\"POST\"");
        StringAssert.Contains(html, "multipart/form-data");
        StringAssert.Contains(html, Convert.ToBase64String(png));
    }

    [TestMethod]
    public void TestLens_EncodePng_ProducesValidPng()
    {
        using var bitmap = new Bitmap(4, 3);
        byte[] png = LensSearchService.EncodePng(bitmap);

        Assert.IsTrue(png.Length > 8, "Expected PNG signature plus image data");
        CollectionAssert.AreEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            png.Take(8).ToArray(),
            "PNG signature mismatch");
    }
}