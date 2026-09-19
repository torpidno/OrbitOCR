using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrbitOCR.UI;

public partial class ActionMenu : UserControl
{
    public event Action? CopyTextRequested;
    public event Action? SearchGoogleRequested;
    public event Action? SearchLensRequested;
    public event Action? CopyImageRequested;
    public event Action? SaveImageRequested;
    public event Action? CloseRequested;

    private readonly SolidColorBrush _blueBadgeText = new(Color.FromRgb(96, 165, 250));
    private readonly SolidColorBrush _blueBadgeBg = new(Color.FromRgb(34, 48, 74));
    private readonly SolidColorBrush _purpleBadgeText = new(Color.FromRgb(192, 132, 252));
    private readonly SolidColorBrush _purpleBadgeBg = new(Color.FromRgb(59, 27, 84));

    public ActionMenu()
    {
        InitializeComponent();

        BtnCopyText.Click += (s, e) => CopyTextRequested?.Invoke();
        BtnSearchGoogle.Click += (s, e) => SearchGoogleRequested?.Invoke();
        BtnSearchLens.Click += (s, e) => SearchLensRequested?.Invoke();
        BtnCopyImage.Click += (s, e) => CopyImageRequested?.Invoke();
        BtnSaveImage.Click += (s, e) => SaveImageRequested?.Invoke();
        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
    }

    public void SetScanning(string message)
    {
        StatusBadge.Text = "🔍 Scanning...";
        StatusBadge.Foreground = _blueBadgeText;
        BadgeBorder.Background = _blueBadgeBg;
        TextPreview.Text = message;

        BtnSearchLens.Visibility = Visibility.Collapsed;
        BtnCopyText.Visibility = Visibility.Collapsed;
        BtnSearchGoogle.Visibility = Visibility.Collapsed;
        BtnCopyImage.Visibility = Visibility.Collapsed;
        BtnSaveImage.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// User clicked or dragged directly over detected text words
    /// </summary>
    public void ShowForTextSelection(string text, int wordCount)
    {
        StatusBadge.Text = $"📝 Text Selected ({wordCount} word{(wordCount == 1 ? "" : "s")})";
        StatusBadge.Foreground = _blueBadgeText;
        BadgeBorder.Background = _blueBadgeBg;

        TextPreview.Text = text.Replace("\r\n", " ").Replace("\n", " ");

        BtnCopyText.Visibility = Visibility.Visible;
        TxtBtnCopyText.Text = "Copy Text";
        BtnSearchGoogle.Visibility = Visibility.Visible;

        // In text mode, image buttons are hidden for a clean focused text experience
        BtnSearchLens.Visibility = Visibility.Collapsed;
        BtnCopyImage.Visibility = Visibility.Collapsed;
        BtnSaveImage.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// User circled or drag-selected an image/region on the screen
    /// </summary>
    public void ShowForImageSelection(int width, int height, string? detectedTextInside = null)
    {
        StatusBadge.Text = $"🖼️ Image ({width} × {height} px)";
        StatusBadge.Foreground = _purpleBadgeText;
        BadgeBorder.Background = _purpleBadgeBg;

        // Visual Search (Lens) is primary
        BtnSearchLens.Visibility = Visibility.Visible;
        BtnCopyImage.Visibility = Visibility.Visible;
        BtnSaveImage.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(detectedTextInside))
        {
            var preview = detectedTextInside.Replace("\r\n", " ").Replace("\n", " ");
            TextPreview.Text = $"Contains: \"{preview}\"";
            BtnCopyText.Visibility = Visibility.Visible;
            TxtBtnCopyText.Text = "Copy Text";
            BtnSearchGoogle.Visibility = Visibility.Visible;
        }
        else
        {
            TextPreview.Text = "Image selected • Search Google Lens or copy image";
            BtnCopyText.Visibility = Visibility.Collapsed;
            BtnSearchGoogle.Visibility = Visibility.Collapsed;
        }
    }
}
