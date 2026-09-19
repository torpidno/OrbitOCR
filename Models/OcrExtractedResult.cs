namespace OrbitOCR.Models;

public class OcrExtractedResult
{
    public string FullText { get; set; } = string.Empty;
    public List<string> Lines { get; set; } = new();
    public int WordCount { get; set; }
    public double? TextAngle { get; set; }
    public bool HasText => !string.IsNullOrWhiteSpace(FullText);
    public string? LanguageTag { get; set; }
}
