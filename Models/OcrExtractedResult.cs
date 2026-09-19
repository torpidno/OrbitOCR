using System.Windows;

namespace OrbitOCR.Models;

public record OcrWordBox(string Text, Rect Rect);

public record OcrLineBox(string Text, Rect Rect, List<OcrWordBox> Words);

public class OcrExtractedResult
{
    public string FullText { get; set; } = string.Empty;
    public List<string> Lines { get; set; } = new();
    public List<OcrWordBox> Words { get; set; } = new();
    public List<OcrLineBox> LineBoxes { get; set; } = new();
    public int WordCount { get; set; }
    public double? TextAngle { get; set; }
    public bool HasText => !string.IsNullOrWhiteSpace(FullText);
    public string? LanguageTag { get; set; }
}
