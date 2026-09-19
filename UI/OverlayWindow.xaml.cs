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

    private Point _startPoint;
    private Rect _selectedRect;
    private bool _isDragging;
    private bool _isResizing;
    private string? _activeResizeHandle;
    private Bitmap? _croppedBitmap;
    private OcrExtractedResult? _lastOcrResult;

    // Freehand Lasso tracking
    private readonly List<Point> _lassoPoints = new();
    private PathGeometry _lassoGeometry = new();
    private PathFigure? _lassoFigure;

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

        // Apply default selection mode from settings
        if (_settingsService.Settings.DefaultSelectionMode == SnipSelectionMode.Lasso)
        {
            RadioLasso.IsChecked = true;
        }
        else
        {
            RadioRect.IsChecked = true;
        }

        // Set frozen screenshot
        FrozenScreenImage.Source = ScreenCaptureService.ConvertToBitmapSource(_desktopBitmap);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;

        // Mouse events on canvas
        SelectionCanvas.MouseDown += OnCanvasMouseDown;
        SelectionCanvas.MouseMove += OnCanvasMouseMove;
        SelectionCanvas.MouseUp += OnCanvasMouseUp;

        // Hook resize handles
        HookResizeHandles();

        // Hook floating action menu events
        HookActionMenuEvents();

        // Mode toggles
        RadioRect.Checked += (s, e) => ResetSelection();
        RadioLasso.Checked += (s, e) => ResetSelection();
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        UpdateDimmedMask(Rect.Empty);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseAndCleanup();
            e.Handled = true;
        }
        else if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.None)
        {
            RadioRect.IsChecked = true;
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.None)
        {
            RadioLasso.IsChecked = true;
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_lastOcrResult != null && _lastOcrResult.HasText)
            {
                CopyTextAction();
                e.Handled = true;
            }
        }
    }

    #region Canvas Mouse & Selection

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // Ignore clicks directly inside the floating menu
        if (FloatingActionMenu.IsMouseOver) return;

        _startPoint = e.GetPosition(SelectionCanvas);
        _isDragging = true;
        _isResizing = false;

        FloatingActionMenu.Visibility = Visibility.Collapsed;
        DimensionsBadge.Visibility = Visibility.Visible;

        if (RadioLasso.IsChecked == true)
        {
            // Lasso mode
            _lassoPoints.Clear();
            _lassoPoints.Add(_startPoint);

            _lassoGeometry = new PathGeometry();
            _lassoFigure = new PathFigure { StartPoint = _startPoint, IsClosed = false };
            _lassoGeometry.Figures.Add(_lassoFigure);
            LassoDrawingPath.Data = _lassoGeometry;
            LassoDrawingPath.Visibility = Visibility.Visible;

            SelectionBorder.Visibility = Visibility.Collapsed;
            HideHandles();
        }
        else
        {
            // Rectangle mode
            LassoDrawingPath.Visibility = Visibility.Collapsed;
            SelectionBorder.Visibility = Visibility.Visible;
            _selectedRect = new Rect(_startPoint, _startPoint);
            UpdateSelectionVisuals(_selectedRect);
        }

        SelectionCanvas.CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var currentPoint = e.GetPosition(SelectionCanvas);

        if (_isResizing && _selectedRect.Width > 0 && _selectedRect.Height > 0)
        {
            ResizeSelection(currentPoint);
            return;
        }

        if (!_isDragging) return;

        if (RadioLasso.IsChecked == true)
        {
            // Lasso / Circling
            _lassoPoints.Add(currentPoint);
            _lassoFigure?.Segments.Add(new LineSegment(currentPoint, true));

            // Compute live bounding box of lasso for mask cutout
            var minX = _lassoPoints.Min(p => p.X);
            var minY = _lassoPoints.Min(p => p.Y);
            var maxX = _lassoPoints.Max(p => p.X);
            var maxY = _lassoPoints.Max(p => p.Y);

            var previewRect = new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
            UpdateDimmedMask(previewRect);
            UpdateDimensionsBadge(previewRect);
        }
        else
        {
            // Standard rectangle
            double x = Math.Min(_startPoint.X, currentPoint.X);
            double y = Math.Min(_startPoint.Y, currentPoint.Y);
            double width = Math.Abs(_startPoint.X - currentPoint.X);
            double height = Math.Abs(_startPoint.Y - currentPoint.Y);

            _selectedRect = new Rect(x, y, width, height);
            UpdateSelectionVisuals(_selectedRect);
        }
    }

    private async void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (_isResizing)
        {
            _isResizing = false;
            _activeResizeHandle = null;
            SelectionCanvas.ReleaseMouseCapture();
            await ProcessSelectionAsync();
            return;
        }

        if (!_isDragging) return;

        _isDragging = false;
        SelectionCanvas.ReleaseMouseCapture();

        if (RadioLasso.IsChecked == true && _lassoPoints.Count > 2)
        {
            // Convert lasso points to tight bounding box with padding
            double minX = _lassoPoints.Min(p => p.X) - 6;
            double minY = _lassoPoints.Min(p => p.Y) - 6;
            double maxX = _lassoPoints.Max(p => p.X) + 6;
            double maxY = _lassoPoints.Max(p => p.Y) + 6;

            minX = Math.Max(0, minX);
            minY = Math.Max(0, minY);
            maxX = Math.Min(SelectionCanvas.ActualWidth, maxX);
            maxY = Math.Min(SelectionCanvas.ActualHeight, maxY);

            _selectedRect = new Rect(minX, minY, Math.Max(20, maxX - minX), Math.Max(20, maxY - minY));
            LassoDrawingPath.Visibility = Visibility.Collapsed;
            SelectionBorder.Visibility = Visibility.Visible;
            UpdateSelectionVisuals(_selectedRect);
        }

        if (_selectedRect.Width < 10 || _selectedRect.Height < 10)
        {
            ResetSelection();
            return;
        }

        ShowHandles();
        await ProcessSelectionAsync();
    }

    #endregion

    #region Visuals & Layout

    private void UpdateSelectionVisuals(Rect rect)
    {
        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);
        SelectionBorder.Width = Math.Max(0, rect.Width);
        SelectionBorder.Height = Math.Max(0, rect.Height);

        UpdateDimmedMask(rect);
        UpdateHandlesPosition(rect);
        UpdateDimensionsBadge(rect);
    }

    private void UpdateDimmedMask(Rect cutout)
    {
        double w = SelectionCanvas.ActualWidth > 0 ? SelectionCanvas.ActualWidth : ActualWidth;
        double h = SelectionCanvas.ActualHeight > 0 ? SelectionCanvas.ActualHeight : ActualHeight;

        if (w <= 0 || h <= 0) return;

        var fullRectGeo = new RectangleGeometry(new Rect(0, 0, w, h));

        if (cutout.Width > 0 && cutout.Height > 0)
        {
            var cutoutGeo = new RectangleGeometry(cutout, 4, 4);
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

        // Place badge above selection if space permits, otherwise below
        double badgeX = rect.Left;
        double badgeY = rect.Top - 26;

        if (badgeY < 10)
        {
            badgeY = rect.Bottom + 6;
        }

        Canvas.SetLeft(DimensionsBadge, Math.Max(4, badgeX));
        Canvas.SetTop(DimensionsBadge, Math.Max(4, badgeY));
    }

    private void PositionActionMenu(Rect rect)
    {
        FloatingActionMenu.Visibility = Visibility.Visible;
        FloatingActionMenu.UpdateLayout();

        double menuW = 460;
        double menuH = 110;

        // Preferred: directly below selection
        double menuX = rect.Left + (rect.Width - menuW) / 2.0;
        double menuY = rect.Bottom + 12;

        // Keep within horizontal canvas boundaries
        if (menuX < 12) menuX = 12;
        if (menuX + menuW > SelectionCanvas.ActualWidth - 12)
        {
            menuX = SelectionCanvas.ActualWidth - menuW - 12;
        }

        // If not enough vertical space below, place above selection
        if (menuY + menuH > SelectionCanvas.ActualHeight - 20)
        {
            menuY = rect.Top - menuH - 12;
            if (menuY < 10)
            {
                // Fallback: clamp inside
                menuY = Math.Max(10, SelectionCanvas.ActualHeight - menuH - 20);
            }
        }

        Canvas.SetLeft(FloatingActionMenu, menuX);
        Canvas.SetTop(FloatingActionMenu, menuY);
    }

    private void ResetSelection()
    {
        _selectedRect = Rect.Empty;
        SelectionBorder.Visibility = Visibility.Collapsed;
        LassoDrawingPath.Visibility = Visibility.Collapsed;
        DimensionsBadge.Visibility = Visibility.Collapsed;
        FloatingActionMenu.Visibility = Visibility.Collapsed;
        HideHandles();
        UpdateDimmedMask(Rect.Empty);
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
        _isDragging = false;
        _activeResizeHandle = handleName;
        FloatingActionMenu.Visibility = Visibility.Collapsed;
        SelectionCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ResizeSelection(Point pt)
    {
        double left = _selectedRect.Left;
        double top = _selectedRect.Top;
        double right = _selectedRect.Right;
        double bottom = _selectedRect.Bottom;

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

        _selectedRect = new Rect(left, top, right - left, bottom - top);
        UpdateSelectionVisuals(_selectedRect);
    }

    private void UpdateHandlesPosition(Rect rect)
    {
        Canvas.SetLeft(HandleTopLeft, rect.Left - 5);
        Canvas.SetTop(HandleTopLeft, rect.Top - 5);

        Canvas.SetLeft(HandleTopRight, rect.Right - 5);
        Canvas.SetTop(HandleTopRight, rect.Top - 5);

        Canvas.SetLeft(HandleBottomLeft, rect.Left - 5);
        Canvas.SetTop(HandleBottomLeft, rect.Bottom - 5);

        Canvas.SetLeft(HandleBottomRight, rect.Right - 5);
        Canvas.SetTop(HandleBottomRight, rect.Bottom - 5);
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

    #region OCR & Actions

    private async Task ProcessSelectionAsync()
    {
        if (_selectedRect.Width <= 4 || _selectedRect.Height <= 4) return;

        // Map WPF DIP coordinates to physical desktop bitmap pixels
        double scaleX = (double)_desktopBitmap.Width / SelectionCanvas.ActualWidth;
        double scaleY = (double)_desktopBitmap.Height / SelectionCanvas.ActualHeight;

        int cropX = (int)Math.Round(_selectedRect.X * scaleX);
        int cropY = (int)Math.Round(_selectedRect.Y * scaleY);
        int cropW = (int)Math.Round(_selectedRect.Width * scaleX);
        int cropH = (int)Math.Round(_selectedRect.Height * scaleY);

        var cropRect = new Rectangle(cropX, cropY, cropW, cropH);

        _croppedBitmap?.Dispose();
        _croppedBitmap = ScreenCaptureService.CropBitmap(_desktopBitmap, cropRect);

        if (_croppedBitmap == null) return;

        PositionActionMenu(_selectedRect);
        FloatingActionMenu.SetLoading();

        // Perform offline WinRT OCR in background
        _lastOcrResult = await _ocrService.RecognizeAsync(_croppedBitmap);

        FloatingActionMenu.SetOcrResult(_lastOcrResult);

        // Auto copy if enabled in settings
        if (_settingsService.Settings.AutoCopyOnSnip && _lastOcrResult.HasText)
        {
            CopyTextAction();
        }
    }

    private void HookActionMenuEvents()
    {
        FloatingActionMenu.CopyTextRequested += CopyTextAction;
        FloatingActionMenu.SearchGoogleRequested += SearchGoogleAction;
        FloatingActionMenu.SearchLensRequested += SearchLensAction;
        FloatingActionMenu.SaveImageRequested += SaveImageAction;
        FloatingActionMenu.CloseRequested += CloseAndCleanup;
    }

    private void CopyTextAction()
    {
        if (_lastOcrResult == null || !_lastOcrResult.HasText) return;

        try
        {
            Clipboard.SetText(_lastOcrResult.FullText);
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
        if (_lastOcrResult == null || !_lastOcrResult.HasText) return;

        try
        {
            string query = Uri.EscapeDataString(_lastOcrResult.FullText);
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
            // Save cropped image to temp file
            string tempDir = Path.Combine(Path.GetTempPath(), "OrbitOCR");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            string tempFile = Path.Combine(tempDir, $"snip_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            _croppedBitmap.Save(tempFile, ImageFormat.Png);

            // Also copy the image to clipboard so user can instantly Ctrl+V into Google Lens / Discord / Slack
            var bitmapSource = ScreenCaptureService.ConvertToBitmapSource(_croppedBitmap);
            Clipboard.SetImage(bitmapSource);

            // Open Google Lens in browser
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://lens.google.com/",
                UseShellExecute = true
            });

            _trayIconService?.ShowNotification("OrbitOCR Lens", "Image copied to clipboard! Press Ctrl+V in Google Lens to search.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OverlayWindow] SearchLens failed: {ex.Message}");
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

        // Release WPF image source bindings to prevent leaks
        FrozenScreenImage.Source = null;

        // Force GC and trim working set memory down to <30 MB idle
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
    }
}
