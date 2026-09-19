using System.Windows.Controls;
using OrbitOCR.Models;

namespace OrbitOCR.UI;

public partial class ActionMenu : UserControl
{
    public event Action? CopyTextRequested;
    public event Action? SearchGoogleRequested;
    public event Action? SearchLensRequested;
    public event Action? SaveImageRequested;
    public event Action? CloseRequested;

    private OcrExtractedResult? _currentResult;

    public ActionMenu()
    {
        InitializeComponent();

        BtnCopyText.Click += (s, e) => CopyTextRequested?.Invoke();
        BtnSearchGoogle.Click += (s, e) => SearchGoogleRequested?.Invoke();
        BtnSearchLens.Click += (s, e) => SearchLensRequested?.Invoke();
        BtnSaveImage.Click += (s, e) => SaveImageRequested?.Invoke();
        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
    }

    public void SetLoading()
    {
        StatusBadge.Text = "⏳ Extracting...";
        TextPreview.Text = "Running offline OCR...";
        BtnCopyText.IsEnabled = false;
        BtnSearchGoogle.IsEnabled = false;
    }

    public void SetOcrResult(OcrExtractedResult result)
    {
        _currentResult = result;

        if (result.HasText)
        {
            StatusBadge.Text = $"⚡ {result.WordCount} word{(result.WordCount == 1 ? "" : "s")}";
            TextPreview.Text = result.FullText.Replace("\r\n", " ").Replace("\n", " ");
            BtnCopyText.IsEnabled = true;
            BtnSearchGoogle.IsEnabled = true;
        }
        else
        {
            StatusBadge.Text = "📷 Image Only";
            TextPreview.Text = "No text detected in selection";
            BtnCopyText.IsEnabled = false;
            BtnSearchGoogle.IsEnabled = false;
        }
    }
}
