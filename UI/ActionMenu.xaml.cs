using System.Windows;
using System.Windows.Controls;

namespace OrbitOCR.UI;

public partial class ActionMenu : UserControl
{
    public event Action? CopyTextRequested;
    public event Action? SearchGoogleRequested;
    public event Action? SearchLensRequested;
    public event Action? CopyImageRequested;
    public event Action? SaveImageRequested;
    public event Action? CloseRequested;

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

    /// <summary>
    /// Gives the contextual primary action the single accent colour; every other
    /// action stays a neutral ghost so the menu never shows two competing CTAs.
    /// </summary>
    private void SetPrimaryAction(Button primary)
    {
        BtnSearchLens.Style = (Style)FindResource("PillGhost");
        BtnCopyText.Style = (Style)FindResource("PillGhost");
        primary.Style = (Style)FindResource("PillPrimary");
    }

    public void SetScanning(string message)
    {
        StatusBadge.Text = "Scanning";
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
        StatusBadge.Text = $"Text · {wordCount} word{(wordCount == 1 ? "" : "s")}";
        TextPreview.Text = text.Replace("\r\n", " ").Replace("\n", " ");

        SetPrimaryAction(BtnCopyText);

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
        StatusBadge.Text = $"Image · {width}×{height}";

        SetPrimaryAction(BtnSearchLens);

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
            TextPreview.Text = "Image selected";
            BtnCopyText.Visibility = Visibility.Collapsed;
            BtnSearchGoogle.Visibility = Visibility.Collapsed;
        }
    }
}
