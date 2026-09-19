using System.Windows;

namespace OrbitOCR.Models;

public record OcrWordBox(string Text, Rect Rect);

public class OcrExtractedResult
{
    public string FullText { get; set; } = string.Empty;
    public List<OcrWordBox> Words { get; set; } = new();
    public bool HasText => !string.IsNullOrWhiteSpace(FullText);
}
