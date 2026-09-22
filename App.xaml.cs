using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using OrbitOCR.Models;
using OrbitOCR.Services;
using OrbitOCR.UI;

namespace OrbitOCR;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    private const string AppMutexName = "OrbitOCR_SingleInstance_Mutex_Session";
    private Mutex? _instanceMutex;

    private SettingsService _settingsService = null!;
    private SoundService _soundService = null!;
    private OcrService _ocrService = null!;
    private ScreenCaptureService _screenCaptureService = null!;
    private HotkeyService _hotkeyService = null!;
    private TrayIconService _trayIconService = null!;

    private OverlayWindow? _activeOverlay;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure single running instance
        _instanceMutex = new Mutex(true, AppMutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show(
                "OrbitOCR is already running in your system tray.\nPress your configured hotkey (default: Ctrl+Shift+S) to trigger.",
                "OrbitOCR Already Running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        InitializeServices();
        TrimWorkingSet();

        bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                                              a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));

        if (!startMinimized)
        {
            HandleOpenSettings();
        }
    }

    private void InitializeServices()
    {
        _settingsService = new SettingsService();
        _soundService = new SoundService(_settingsService);
        _ocrService = new OcrService();
        _ocrService.InitializeEngine(_settingsService.Settings.PreferredOcrLanguage);

        _screenCaptureService = new ScreenCaptureService();
        _hotkeyService = new HotkeyService(_settingsService);

        string hotkeyDisplay = FormatHotkeyString(_settingsService.Settings);
        _trayIconService = new TrayIconService(hotkeyDisplay);

        // Wire service events
        _hotkeyService.HotkeyTriggered += HandleSnipTriggered;
        _trayIconService.TriggerSnipRequested += HandleSnipTriggered;
        _trayIconService.OpenSettingsRequested += HandleOpenSettings;
        _trayIconService.ExitRequested += HandleExit;

        _settingsService.SettingsChanged += HandleSettingsChanged;

        // Show welcome balloon on first launch if needed
        _trayIconService.ShowNotification(
            "OrbitOCR Running",
            $"Press {hotkeyDisplay} anytime to freeze the screen and snip/circle text.");
    }

    private void HandleSettingsChanged(AppSettings newSettings)
    {
        string hotkeyDisplay = FormatHotkeyString(newSettings);
        _trayIconService.UpdateHotkeyDisplay(hotkeyDisplay);
        _ocrService.InitializeEngine(newSettings.PreferredOcrLanguage);
    }

    private async void HandleSnipTriggered()
    {
        if (_activeOverlay != null && _activeOverlay.IsVisible)
        {
            _activeOverlay.Activate();
            return;
        }

        // Brief delay to let menus/focus settle before screen capture
        await Task.Delay(100);

        try
        {
            var (bitmap, bounds) = _screenCaptureService.CaptureEntireDesktop();

            _activeOverlay = new OverlayWindow(
                bitmap,
                bounds,
                _ocrService,
                _soundService,
                _settingsService,
                _trayIconService);

            _activeOverlay.Closed += (s, e) =>
            {
                _activeOverlay = null;
                TrimWorkingSet();
            };

            _activeOverlay.Show();
            _activeOverlay.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Snip trigger error: {ex.Message}");
            _trayIconService.ShowNotification("OrbitOCR Error", "Failed to capture screen: " + ex.Message);
        }
    }

    private void HandleOpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            _settingsService,
            _ocrService,
            s => _hotkeyService.RegisterFromSettings(s),
            HandleSnipTriggered,
            HandleExit);

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void HandleExit()
    {
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIconService?.Dispose();

        if (_instanceMutex != null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch
            {
                // Ignore if mutex was not acquired
            }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }

    private void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
        }
        catch
        {
            // Ignore non-critical memory trimming errors
        }
    }

    public static string FormatHotkeyString(AppSettings settings)
    {
        var parts = new List<string>();
        if (settings.HotkeyCtrl) parts.Add("Ctrl");
        if (settings.HotkeyShift) parts.Add("Shift");
        if (settings.HotkeyAlt) parts.Add("Alt");
        if (settings.HotkeyWin) parts.Add("Win");
        parts.Add(settings.HotkeyKey);
        return string.Join("+", parts);
    }
}
