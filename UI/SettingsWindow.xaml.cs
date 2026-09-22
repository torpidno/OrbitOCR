using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OrbitOCR.Models;
using OrbitOCR.Services;

namespace OrbitOCR.UI;

public partial class SettingsWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private readonly SettingsService _settingsService;
    private readonly OcrService _ocrService;
    private readonly Func<AppSettings, bool> _registerHotkey;
    private readonly Action _onTriggerSnip;
    private readonly Action _onExit;

    // Pending (unsaved) shortcut, edited by the recorder
    private bool _isRecording;
    private bool _currentCtrl;
    private bool _currentShift;
    private bool _currentAlt;
    private bool _currentWin;
    private string _currentKey = "S";

    public SettingsWindow(
        SettingsService settingsService,
        OcrService ocrService,
        Func<AppSettings, bool> registerHotkey,
        Action onTriggerSnip,
        Action onExit)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _ocrService = ocrService;
        _registerHotkey = registerHotkey;
        _onTriggerSnip = onTriggerSnip;
        _onExit = onExit;

        PopulateLanguages();
        LoadCurrentSettings();
        HookEvents();
    }

    private void PopulateLanguages()
    {
        var items = new List<string> { "Default (Windows User Profile Languages)" };
        items.AddRange(_ocrService.GetAvailableLanguages());

        CmbOcrLanguage.ItemsSource = items;
    }

    /// <summary>
    /// Populates the window from persisted settings. Also used to discard pending edits.
    /// </summary>
    private void LoadCurrentSettings()
    {
        var s = _settingsService.Settings;

        _currentCtrl = s.HotkeyCtrl;
        _currentShift = s.HotkeyShift;
        _currentAlt = s.HotkeyAlt;
        _currentWin = s.HotkeyWin;
        _currentKey = s.HotkeyKey;

        if (!string.IsNullOrEmpty(s.PreferredOcrLanguage) && CmbOcrLanguage.Items.Contains(s.PreferredOcrLanguage))
        {
            CmbOcrLanguage.SelectedItem = s.PreferredOcrLanguage;
        }
        else
        {
            CmbOcrLanguage.SelectedIndex = 0;
        }

        ChkAutoCopy.IsChecked = s.AutoCopyOnSnip;
        ChkPlaySounds.IsChecked = s.PlaySounds;
        ChkStartWithWindows.IsChecked = s.StartWithWindows;

        UpdateKeycapsDisplay();
    }

    private void HookEvents()
    {
        BtnRecordShortcut.Click += (s, e) => ToggleRecording();
        PreviewKeyDown += OnWindowPreviewKeyDown;

        BtnTestSnip.Click += OnTestSnipClicked;
        BtnSave.Click += OnSaveClicked;
        BtnCancel.Click += (s, e) => CancelAndHide();
        BtnExitApp.Click += (s, e) => _onExit();
    }

    private void CancelAndHide()
    {
        LoadCurrentSettings();
        HideStatus();
        Hide();
    }

    #region Shortcut Recorder

    private void ToggleRecording()
    {
        if (_isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        _isRecording = true;
        RecordDot.Fill = (Brush)FindResource("DangerBrush");
        BtnRecordShortcut.Background = (Brush)FindResource("DangerSoftBrush");
        TxtRecordButton.Text = "Listening…";
        Focus();
    }

    private void StopRecording()
    {
        _isRecording = false;
        RecordDot.Fill = (Brush)FindResource("TextTertiaryBrush");
        BtnRecordShortcut.Background = (Brush)FindResource("SurfaceRaisedBrush");
        TxtRecordButton.Text = "Record";
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecording) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Escape cancels recording instead of being captured as a shortcut
        if (key == Key.Escape)
        {
            StopRecording();
            e.Handled = true;
            return;
        }

        // Skip pure modifier key presses
        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin)
        {
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;

        string keyStr = key.ToString();
        if (keyStr.Length == 2 && keyStr.StartsWith("D") && char.IsDigit(keyStr[1]))
        {
            keyStr = keyStr[1].ToString();
        }

        _currentCtrl = ctrl;
        _currentShift = shift;
        _currentAlt = alt;
        _currentWin = win;
        _currentKey = keyStr;

        UpdateKeycapsDisplay();
        StopRecording();

        e.Handled = true;
    }

    private void UpdateKeycapsDisplay()
    {
        KeycapsContainer.Children.Clear();

        var parts = new List<string>();
        if (_currentCtrl) parts.Add("Ctrl");
        if (_currentShift) parts.Add("Shift");
        if (_currentAlt) parts.Add("Alt");
        if (_currentWin) parts.Add("Win");
        parts.Add(_currentKey);

        for (int i = 0; i < parts.Count; i++)
        {
            var badge = new Border
            {
                Background = (Brush)FindResource("SurfaceRaisedBrush"),
                BorderBrush = (Brush)FindResource("BorderStrongBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 5, 0)
            };

            var text = new TextBlock
            {
                Text = parts[i],
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            };

            badge.Child = text;
            KeycapsContainer.Children.Add(badge);

            if (i < parts.Count - 1)
            {
                var plus = new TextBlock
                {
                    Text = "+",
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                KeycapsContainer.Children.Add(plus);
            }
        }
    }

    #endregion

    private void OnTestSnipClicked(object sender, RoutedEventArgs e)
    {
        Hide();
        _onTriggerSnip();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!_currentCtrl && !_currentShift && !_currentAlt && !_currentWin && !_currentKey.StartsWith("F"))
        {
            MessageBox.Show(
                "Please select at least one modifier key (Ctrl, Shift, Alt, or Win) or a function key (F1-F12) to avoid accidental triggers.",
                "Invalid Shortcut",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var newSettings = new AppSettings
        {
            HotkeyCtrl = _currentCtrl,
            HotkeyShift = _currentShift,
            HotkeyAlt = _currentAlt,
            HotkeyWin = _currentWin,
            HotkeyKey = _currentKey,
            AutoCopyOnSnip = ChkAutoCopy.IsChecked == true,
            PlaySounds = ChkPlaySounds.IsChecked == true,
            StartWithWindows = ChkStartWithWindows.IsChecked == true
        };

        if (CmbOcrLanguage.SelectedIndex > 0)
        {
            newSettings.PreferredOcrLanguage = CmbOcrLanguage.SelectedItem?.ToString();
        }
        else
        {
            newSettings.PreferredOcrLanguage = null;
        }

        _settingsService.SaveSettings(newSettings);
        _ocrService.InitializeEngine(newSettings.PreferredOcrLanguage);

        string hotkeyDisplay = App.FormatHotkeyString(newSettings);

        // Settings are persisted, but the shortcut only works if Windows accepts it.
        // Report the failure instead of claiming success.
        if (!_registerHotkey(newSettings))
        {
            ShowStatus($"Failed to register {hotkeyDisplay} — the shortcut may already be in use by another application. Try a different combination.");
            return;
        }

        HideStatus();

        MessageBox.Show(
            $"Settings applied!\nNew Global Shortcut: {hotkeyDisplay}\n\nPress this shortcut anytime anywhere in Windows to trigger OrbitOCR.",
            "OrbitOCR Settings Saved",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// Shows a non-blocking, form-level warning. The window stays open so the user can
    /// immediately record a different shortcut.
    /// </summary>
    private void ShowStatus(string message)
    {
        TxtStatus.Text = message;
        TxtStatus.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        TxtStatus.Text = string.Empty;
        TxtStatus.Visibility = Visibility.Collapsed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBar();
    }

    /// <summary>
    /// The window is dark, so the OS title bar must match. DWMWA_USE_IMMERSIVE_DARK_MODE
    /// is 20 on Windows 11 22000+ and 19 on earlier builds; caption/text color are Win11 only.
    /// </summary>
    private void ApplyDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDark = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int));
            }

            int caption = 0x00202020; // #202020 in 0x00BBGGRR
            int text = 0x00FFFFFF;    // #FFFFFF in 0x00BBGGRR
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
        }
        catch
        {
            // Title bar theming is cosmetic; ignore on unsupported builds
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing hides to tray; discard unsaved edits so Cancel and the X button agree.
        e.Cancel = true;
        LoadCurrentSettings();
        HideStatus();
        Hide();
    }
}
