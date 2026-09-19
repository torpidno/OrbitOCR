using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitOCR.Models;
using OrbitOCR.Services;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;

namespace OrbitOCR.UI;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    private readonly Bitmap _desktopBitmap;
    private readonly ScreenCaptureService.VirtualScreenBounds _screenBounds;
    private readonly OcrService _ocrService;
    private readonly SoundService _soundService;
    private readonly SettingsService _settingsService;
    private readonly TrayIconService? _trayIconService;

    // Word mapping model in DIP coordinates
    private record CanvasWord(string Text, Rect CanvasRect, OcrWordBox Original);
    private List<CanvasWord> _canvasWords = new();
    private OcrExtractedResult? _fullScanResult;

    // Interaction states
    private bool _isTextSelecting;
    private bool _isCircling;
    private bool _isResizing;
    private string? _activeResizeHandle;
    private Point _startPoint;

    // Selected text state
    private readonly List<CanvasWord> _selectedWords = new();
    private string _currentSelectedText = string.Empty;

    // Selected image/circled region state
    private Rect _circledRect = Rect.Empty;
    private readonly List<Point> _lassoPoints = new();
    private PathGeometry _lassoGeometry = new();
    private PathFigure? _lassoFigure;
    private Bitmap? _croppedBitmap;

    public OverlayWindow(
        Bitmap desktopBitmap,
        ScreenCaptureService.VirtualScreenBounds bounds,
        OcrService ocrService,
        SoundService soundService,
        SettingsService settingsService,
        TrayIconService? trayIconService)
    {
        InitializeComponent();

        _desktopBitmap = desktopBitmap;
        _screenBounds = bounds;
        _ocrService = ocrService;
        _soundService = soundService;
        _settingsService = settingsService;
        _trayIconService = trayIconService;

        // Display frozen screenshot
        FrozenScreenImage.Source = ScreenCaptureService.ConvertToBitmapSource(_desktopBitmap);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;

        SelectionCanvas.MouseDown += OnCanvasMouseDown;
        SelectionCanvas.MouseMove += OnCanvasMouseMove;
        SelectionCanvas.MouseUp += OnCanvasMouseUp;

        HookResizeHandles();
        HookActionMenuEvents();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(
            hwnd,
            HWND_TOPMOST,
            _screenBounds.Left,
            _screenBounds.Top,
            _screenBounds.Width,
            _screenBounds.Height,
            SWP_SHOWWINDOW);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        UpdateDimmedMask(Rect.Empty);

        // Immediate background full screen OCR
        await RunFullScreenScanAsync();
    }

    private async Task RunFullScreenScanAsync()
    {
        try
        {
            TxtStatusMessage.Text = "Scanning screen for text…";

            _fullScanResult = await _ocrService.RecognizeAsync(_desktopBitmap);

            // Map physical pixel word boxes to canvas DIP coordinates
            double scaleX = (double)_desktopBitmap.Width / ActualWidth;
            double scaleY = (double)_desktopBitmap.Height / ActualHeight;

            _canvasWords = _fullScanResult.Words.Select(w => new CanvasWord(
                w.Text,
                new Rect(
                    w.Rect.X / scaleX,
                    w.Rect.Y / scaleY,
                    w.Rect.Width / scaleX,
                    w.Rect.Height / scaleY),
                w
            )).ToList();

            TxtStatusMessage.Text = $"Scanned {_canvasWords.Count} words • Select text or circle an image";

            // Mark every detected word with a subtle underline rather than a box,
            // so the screen stays readable until the user actually selects something.
            WordChipsCanvas.Children.Clear();
            foreach (var word in _canvasWords)
            {
                var underline = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(90, 96, 205, 255)),
                    CornerRadius = new CornerRadius(1),
                    Height = 2,
                    Width = Math.Max(4, word.CanvasRect.Width)
                };
                Canvas.SetLeft(underline, word.CanvasRect.Left);
                Canvas.SetTop(underline, word.CanvasRect.Bottom - 1);
                WordChipsCanvas.Children.Add(underline);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayWindow] Full screen scan error: {ex.Message}");
            TxtStatusMessage.Text = "Circle any area to search with Google Lens";
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseAndCleanup();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!string.IsNullOrWhiteSpace(_currentSelectedText))
            {
                CopyTextAction();
                e.Handled = true;
            }
        }
    }

    #region Mouse Interactions (Text Selection vs Image Circling)

    private CanvasWord? FindWordAtPoint(Point pt)
    {
        // Generous 8px horizontal, 6px vertical hit-test margin for effortless text clicking
        return _canvasWords.FirstOrDefault(w =>
        {
            var inflated = w.CanvasRect;
            inflated.Inflate(8, 6);
            return inflated.Contains(pt);
        });
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (FloatingActionMenu.IsMouseOver) return;

        _startPoint = e.GetPosition(SelectionCanvas);
        var wordUnderCursor = FindWordAtPoint(_startPoint);

        FloatingActionMenu.Visibility = Visibility.Collapsed;
        HideImageSelectionVisuals();

        if (wordUnderCursor != null)
        {
            // === MODE 1: TEXT SELECTION ===
            _isTextSelecting = true;
            _isCircling = false;
            _isResizing = false;

            _selectedWords.Clear();
            _selectedWords.Add(wordUnderCursor);

            RenderSelectedWordsVisuals();
            UpdateDimmedMaskForWords();

            _currentSelectedText = wordUnderCursor.Text;
            FloatingActionMenu.ShowForTextSelection(_currentSelectedText, 1);
            PositionActionMenuForWords();
        }
        else
        {
            // === MODE 2: IMAGE CIRCLING ===
            _isCircling = true;
            _isTextSelecting = false;
            _isResizing = false;

            ClearTextSelection();

            _lassoPoints.Clear();
            _lassoPoints.Add(_startPoint);

            _lassoGeometry = new PathGeometry();
            _lassoFigure = new PathFigure { StartPoint = _startPoint, IsClosed = false };
            _lassoGeometry.Figures.Add(_lassoFigure);
            LassoDrawingPath.Data = _lassoGeometry;
            LassoDrawingPath.Visibility = Visibility.Visible;

            DimensionsBadge.Visibility = Visibility.Visible;
        }

        SelectionCanvas.CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var currentPoint = e.GetPosition(SelectionCanvas);

        if (_isResizing)
        {
            ResizeCircledImage(currentPoint);
            return;
        }

        if (_isTextSelecting)
        {
            // Dragging across text words to select phrases or sentences
            var dragRect = new Rect(
                Math.Min(_startPoint.X, currentPoint.X),
                Math.Min(_startPoint.Y, currentPoint.Y),
                Math.Max(1, Math.Abs(_startPoint.X - currentPoint.X)),
                Math.Max(1, Math.Abs(_startPoint.Y - currentPoint.Y)));

            // Expand slightly to easily capture adjacent words
            dragRect.Inflate(4, 4);

            var wordsInSelection = _canvasWords.Where(w => w.CanvasRect.IntersectsWith(dragRect)).ToList();

            if (wordsInSelection.Count > 0)
            {
                // Sort words in reading order: top-to-bottom lines, left-to-right
                wordsInSelection = wordsInSelection
                    .OrderBy(w => Math.Round(w.CanvasRect.Top / 14.0) * 14.0)
                    .ThenBy(w => w.CanvasRect.Left)
                    .ToList();

                _selectedWords.Clear();
                _selectedWords.AddRange(wordsInSelection);

                RenderSelectedWordsVisuals();
                UpdateDimmedMaskForWords();

                _currentSelectedText = string.Join(" ", _selectedWords.Select(w => w.Text));
                FloatingActionMenu.ShowForTextSelection(_currentSelectedText, _selectedWords.Count);
                PositionActionMenuForWords();
            }
            return;
        }

        if (_isCircling)
        {
            // Circling an image / freehand lasso
            _lassoPoints.Add(currentPoint);
            _lassoFigure?.Segments.Add(new LineSegment(currentPoint, true));

            var minX = _lassoPoints.Min(p => p.X);
            var minY = _lassoPoints.Min(p => p.Y);
            var maxX = _lassoPoints.Max(p => p.X);
            var maxY = _lassoPoints.Max(p => p.Y);

            var previewRect = new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
            UpdateDimmedMask(previewRect);
            UpdateDimensionsBadge(previewRect);
            return;
        }

        // Hover effect when mouse is moving freely
        var hoveredWord = FindWordAtPoint(currentPoint);
        if (hoveredWord != null)
        {
            Cursor = Cursors.IBeam;
            Canvas.SetLeft(HoverWordBorder, hoveredWord.CanvasRect.Left - 2);
            Canvas.SetTop(HoverWordBorder, hoveredWord.CanvasRect.Top - 1);
            HoverWordBorder.Width = hoveredWord.CanvasRect.Width + 4;
            HoverWordBorder.Height = hoveredWord.CanvasRect.Height + 2;
            HoverWordBorder.Visibility = Visibility.Visible;
        }
        else
        {
            Cursor = Cursors.Cross;
            HoverWordBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (_isResizing)
        {
            _isResizing = false;
            _activeResizeHandle = null;
            SelectionCanvas.ReleaseMouseCapture();
            ProcessCircledImage();
            return;
        }

        if (_isTextSelecting)
        {
            _isTextSelecting = false;
            SelectionCanvas.ReleaseMouseCapture();

            if (_selectedWords.Count > 0)
            {
                PositionActionMenuForWords();

                if (_settingsService.Settings.AutoCopyOnSnip)
                {
                    CopyTextAction();
                }
            }
            return;
        }

        if (!_isCircling) return;

        _isCircling = false;
        SelectionCanvas.ReleaseMouseCapture();
        LassoDrawingPath.Visibility = Visibility.Collapsed;

        if (_lassoPoints.Count > 2)
        {
            double minX = _lassoPoints.Min(p => p.X) - 6;
            double minY = _lassoPoints.Min(p => p.Y) - 6;
            double maxX = _lassoPoints.Max(p => p.X) + 6;
            double maxY = _lassoPoints.Max(p => p.Y) + 6;

            minX = Math.Max(0, minX);
            minY = Math.Max(0, minY);
            maxX = Math.Min(SelectionCanvas.ActualWidth, maxX);
            maxY = Math.Min(SelectionCanvas.ActualHeight, maxY);

            _circledRect = new Rect(minX, minY, Math.Max(20, maxX - minX), Math.Max(20, maxY - minY));

            if (_circledRect.Width < 12 || _circledRect.Height < 12)
            {
                HideImageSelectionVisuals();
                UpdateDimmedMask(Rect.Empty);
                return;
            }

            ShowImageSelectionVisuals(_circledRect);
            ProcessCircledImage();
        }
    }

    #endregion

    #region Text Visuals

    private void RenderSelectedWordsVisuals()
    {
        SelectedWordsCanvas.Children.Clear();

        foreach (var word in _selectedWords)
        {
            var highlight = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(70, 96, 205, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 96, 205, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                Width = word.CanvasRect.Width + 4,
                Height = word.CanvasRect.Height + 2
            };

            Canvas.SetLeft(highlight, word.CanvasRect.Left - 2);
            Canvas.SetTop(highlight, word.CanvasRect.Top - 1);
            SelectedWordsCanvas.Children.Add(highlight);
        }
    }

    private void UpdateDimmedMaskForWords()
    {
        if (_selectedWords.Count == 0)
        {
            UpdateDimmedMask(Rect.Empty);
            return;
        }

        double minX = _selectedWords.Min(w => w.CanvasRect.Left) - 4;
        double minY = _selectedWords.Min(w => w.CanvasRect.Top) - 2;
        double maxX = _selectedWords.Max(w => w.CanvasRect.Right) + 4;
        double maxY = _selectedWords.Max(w => w.CanvasRect.Bottom) + 2;

        var bounds = new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        UpdateDimmedMask(bounds);
    }

    private void PositionActionMenuForWords()
    {
        if (_selectedWords.Count == 0) return;

        double minX = _selectedWords.Min(w => w.CanvasRect.Left);
        double minY = _selectedWords.Min(w => w.CanvasRect.Top);
        double maxX = _selectedWords.Max(w => w.CanvasRect.Right);
        double maxY = _selectedWords.Max(w => w.CanvasRect.Bottom);

        var bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
        PositionActionMenu(bounds);
    }

    private void ClearTextSelection()
    {
        _selectedWords.Clear();
        SelectedWordsCanvas.Children.Clear();
        _currentSelectedText = string.Empty;
    }

    #endregion

    #region Image Selection & Circling

    private void ProcessCircledImage()
    {
        if (_circledRect.Width <= 4 || _circledRect.Height <= 4) return;

        double scaleX = (double)_desktopBitmap.Width / SelectionCanvas.ActualWidth;
        double scaleY = (double)_desktopBitmap.Height / SelectionCanvas.ActualHeight;

        int cropX = (int)Math.Round(_circledRect.X * scaleX);
        int cropY = (int)Math.Round(_circledRect.Y * scaleY);
        int cropW = (int)Math.Round(_circledRect.Width * scaleX);
        int cropH = (int)Math.Round(_circledRect.Height * scaleY);

        var cropRect = new Rectangle(cropX, cropY, cropW, cropH);

        _croppedBitmap?.Dispose();
        _croppedBitmap = ScreenCaptureService.CropBitmap(_desktopBitmap, cropRect);

        if (_croppedBitmap == null) return;

        // Check if any recognized text is inside the circled region using IntersectsWith
        var wordsInside = _canvasWords
            .Where(w => _circledRect.IntersectsWith(w.CanvasRect))
            .OrderBy(w => Math.Round(w.CanvasRect.Top / 14.0) * 14.0)
            .ThenBy(w => w.CanvasRect.Left)
            .Select(w => w.Text)
            .ToList();

        string? textInside = wordsInside.Count > 0 ? string.Join(" ", wordsInside) : null;
        if (!string.IsNullOrEmpty(textInside))
        {
            _currentSelectedText = textInside;
        }

        // Show menu in IMAGE MODE immediately
        FloatingActionMenu.ShowForImageSelection(cropW, cropH, textInside);
        PositionActionMenu(_circledRect);

        // Run dedicated 2x upscaled OCR on the cropped bitmap to extract all text with maximum accuracy
        _ = RunDedicatedCropOcrAsync(_croppedBitmap, cropW, cropH);
    }

    private async Task RunDedicatedCropOcrAsync(Bitmap croppedBitmap, int cropW, int cropH)
    {
        try
        {
            var cropResult = await _ocrService.RecognizeAsync(croppedBitmap);
            if (cropResult.HasText)
            {
                _currentSelectedText = cropResult.FullText;
                FloatingActionMenu.ShowForImageSelection(cropW, cropH, cropResult.FullText);
                PositionActionMenu(_circledRect);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayWindow] Crop OCR error: {ex.Message}");
        }
    }

    private void ShowImageSelectionVisuals(Rect rect)
    {
        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);
        SelectionBorder.Width = Math.Max(0, rect.Width);
        SelectionBorder.Height = Math.Max(0, rect.Height);
        SelectionBorder.Visibility = Visibility.Visible;

        UpdateDimmedMask(rect);
        UpdateHandlesPosition(rect);
        UpdateDimensionsBadge(rect);
        ShowHandles();
    }

    private void HideImageSelectionVisuals()
    {
        SelectionBorder.Visibility = Visibility.Collapsed;
        DimensionsBadge.Visibility = Visibility.Collapsed;
        HideHandles();
    }

    private void UpdateDimmedMask(Rect cutout)
    {
        double w = SelectionCanvas.ActualWidth > 0 ? SelectionCanvas.ActualWidth : ActualWidth;
        double h = SelectionCanvas.ActualHeight > 0 ? SelectionCanvas.ActualHeight : ActualHeight;

        if (w <= 0 || h <= 0) return;

        var fullRectGeo = new RectangleGeometry(new Rect(0, 0, w, h));

        if (cutout.Width > 0 && cutout.Height > 0)
        {
            var cutoutGeo = new RectangleGeometry(cutout, 6, 6);
            DimmedMaskPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, fullRectGeo, cutoutGeo);
        }
        else
        {
            DimmedMaskPath.Data = fullRectGeo;
        }
    }

    private void UpdateDimensionsBadge(Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            DimensionsBadge.Visibility = Visibility.Collapsed;
            return;
        }

        DimensionsBadge.Visibility = Visibility.Visible;
        DimensionsText.Text = $"{(int)rect.Width} × {(int)rect.Height} px";

        double badgeX = rect.Left;
        double badgeY = rect.Top - 26;
        if (badgeY < 10) badgeY = rect.Bottom + 6;

        Canvas.SetLeft(DimensionsBadge, Math.Max(4, badgeX));
        Canvas.SetTop(DimensionsBadge, Math.Max(4, badgeY));
    }

    private void PositionActionMenu(Rect anchor)
    {
        FloatingActionMenu.Visibility = Visibility.Visible;
        FloatingActionMenu.UpdateLayout();

        // Measure the real menu rather than guessing a size, so the pill always fits its content
        double menuW = FloatingActionMenu.ActualWidth > 0 ? FloatingActionMenu.ActualWidth : FloatingActionMenu.DesiredSize.Width;
        double menuH = FloatingActionMenu.ActualHeight > 0 ? FloatingActionMenu.ActualHeight : FloatingActionMenu.DesiredSize.Height;

        double menuX = anchor.Left + (anchor.Width - menuW) / 2.0;
        double menuY = anchor.Bottom + 12;

        if (menuX < 12) menuX = 12;
        if (menuX + menuW > SelectionCanvas.ActualWidth - 12)
        {
            menuX = SelectionCanvas.ActualWidth - menuW - 12;
        }

        if (menuY + menuH > SelectionCanvas.ActualHeight - 20)
        {
            menuY = anchor.Top - menuH - 12;
            if (menuY < 10)
            {
                menuY = Math.Max(10, SelectionCanvas.ActualHeight - menuH - 20);
            }
        }

        Canvas.SetLeft(FloatingActionMenu, menuX);
        Canvas.SetTop(FloatingActionMenu, menuY);
    }

    #endregion

    #region Resize Handles

    private void HookResizeHandles()
    {
        HandleTopLeft.MouseDown += (s, e) => StartHandleDrag("TL", e);
        HandleTopRight.MouseDown += (s, e) => StartHandleDrag("TR", e);
        HandleBottomLeft.MouseDown += (s, e) => StartHandleDrag("BL", e);
        HandleBottomRight.MouseDown += (s, e) => StartHandleDrag("BR", e);
    }

    private void StartHandleDrag(string handleName, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isResizing = true;
        _isCircling = false;
        _isTextSelecting = false;
        _activeResizeHandle = handleName;
        FloatingActionMenu.Visibility = Visibility.Collapsed;
        SelectionCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeCircledImage(Point pt)
    {
        double left = _circledRect.Left;
        double top = _circledRect.Top;
        double right = _circledRect.Right;
        double bottom = _circledRect.Bottom;

        switch (_activeResizeHandle)
        {
            case "TL":
                left = Math.Min(pt.X, right - 10);
                top = Math.Min(pt.Y, bottom - 10);
                break;
            case "TR":
                right = Math.Max(pt.X, left + 10);
                top = Math.Min(pt.Y, bottom - 10);
                break;
            case "BL":
                left = Math.Min(pt.X, right - 10);
                bottom = Math.Max(pt.Y, top + 10);
                break;
            case "BR":
                right = Math.Max(pt.X, left + 10);
                bottom = Math.Max(pt.Y, top + 10);
                break;
        }

        _circledRect = new Rect(left, top, right - left, bottom - top);
        ShowImageSelectionVisuals(_circledRect);
    }

    private void UpdateHandlesPosition(Rect rect)
    {
        // Handles are 24x24 hit targets, so offset by half their size to centre the dot on the corner
        const double half = 12;

        Canvas.SetLeft(HandleTopLeft, rect.Left - half);
        Canvas.SetTop(HandleTopLeft, rect.Top - half);

        Canvas.SetLeft(HandleTopRight, rect.Right - half);
        Canvas.SetTop(HandleTopRight, rect.Top - half);

        Canvas.SetLeft(HandleBottomLeft, rect.Left - half);
        Canvas.SetTop(HandleBottomLeft, rect.Bottom - half);

        Canvas.SetLeft(HandleBottomRight, rect.Right - half);
        Canvas.SetTop(HandleBottomRight, rect.Bottom - half);
    }

    private void ShowHandles()
    {
        HandleTopLeft.Visibility = Visibility.Visible;
        HandleTopRight.Visibility = Visibility.Visible;
        HandleBottomLeft.Visibility = Visibility.Visible;
        HandleBottomRight.Visibility = Visibility.Visible;
    }

    private void HideHandles()
    {
        HandleTopLeft.Visibility = Visibility.Collapsed;
        HandleTopRight.Visibility = Visibility.Collapsed;
        HandleBottomLeft.Visibility = Visibility.Collapsed;
        HandleBottomRight.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Action Menu Execution

    private void HookActionMenuEvents()
    {
        FloatingActionMenu.CopyTextRequested += CopyTextAction;
        FloatingActionMenu.SearchGoogleRequested += SearchGoogleAction;
        FloatingActionMenu.SearchLensRequested += SearchLensAction;
        FloatingActionMenu.CopyImageRequested += CopyImageAction;
        FloatingActionMenu.SaveImageRequested += SaveImageAction;
        FloatingActionMenu.CloseRequested += CloseAndCleanup;
    }

    private void CopyTextAction()
    {
        if (string.IsNullOrWhiteSpace(_currentSelectedText)) return;

        try
        {
            Clipboard.SetText(_currentSelectedText);
            _soundService.PlayCopySuccess();
            _trayIconService?.ShowNotification("OrbitOCR", "✓ Copied text to clipboard!");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayWindow] CopyText failed: {ex.Message}");
        }

        CloseAndCleanup();
    }

    private void SearchGoogleAction()
    {
        if (string.IsNullOrWhiteSpace(_currentSelectedText)) return;

        try
        {
            string query = Uri.EscapeDataString(_currentSelectedText);
            string url = $"https://www.google.com/search?q={query}";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayWindow] SearchGoogle failed: {ex.Message}");
        }

        CloseAndCleanup();
    }

    private void SearchLensAction()
    {
        if (_croppedBitmap == null) return;

        try
        {
            // The browser performs the upload and lands on the results page - no clipboard paste.
            // LensSearchService owns the temp payload's lifetime.
            LensSearchService.LaunchSearch(_croppedBitmap);

            _soundService.PlayCopySuccess();
            _trayIconService?.ShowNotification("OrbitOCR Lens", "Opening Google Lens results…");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayWindow] SearchLens failed: {ex.Message}");
            CopyImageAndOpenLensHome();
        }

        CloseAndCleanup();
    }

    /// <summary>
    /// Last-resort path when the browser hand-off fails: keep the old clipboard behaviour so
    /// the user can still paste the image into Lens manually.
    /// </summary>
    private void CopyImageAndOpenLensHome()
    {
        try
        {
            Clipboard.SetImage(ScreenCaptureService.ConvertToBitmapSource(_croppedBitmap!));
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://lens.google.com/",
                UseShellExecute = true
            });
            _trayIconService?.ShowNotification("OrbitOCR Lens", "Image copied - press Ctrl+V in Google Lens.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayWindow] Lens fallback failed: {ex.Message}");
            _trayIconService?.ShowNotification("OrbitOCR Lens", "Lens search failed.");
        }
    }

    private void CopyImageAction()
    {
        if (_croppedBitmap == null) return;

        try
        {
            var bitmapSource = ScreenCaptureService.ConvertToBitmapSource(_croppedBitmap);
            Clipboard.SetImage(bitmapSource);
            _soundService.PlayCopySuccess();
            _trayIconService?.ShowNotification("OrbitOCR", "✓ Image copied to clipboard!");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayWindow] CopyImage failed: {ex.Message}");
        }

        CloseAndCleanup();
    }

    private void SaveImageAction()
    {
        if (_croppedBitmap == null) return;

        try
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg",
                FileName = $"OrbitOCR_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                DefaultExt = ".png"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var format = saveDialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? ImageFormat.Jpeg
                    : ImageFormat.Png;

                _croppedBitmap.Save(saveDialog.FileName, format);
                _soundService.PlayCopySuccess();
                _trayIconService?.ShowNotification("OrbitOCR", "✓ Image saved successfully!");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayWindow] SaveImage failed: {ex.Message}");
        }

        CloseAndCleanup();
    }

    #endregion

    private void CloseAndCleanup()
    {
        try
        {
            Close();
        }
        catch
        {
            // Ignore if already closing
        }

        _desktopBitmap.Dispose();
        _croppedBitmap?.Dispose();

        FrozenScreenImage.Source = null;
        WordChipsCanvas.Children.Clear();
        SelectedWordsCanvas.Children.Clear();
        _canvasWords.Clear();
        _selectedWords.Clear();

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
    }
}
