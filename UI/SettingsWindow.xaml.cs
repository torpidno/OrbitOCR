using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OrbitOCR.Models;
using OrbitOCR.Services;

namespace OrbitOCR.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly OcrService _ocrService;
    private readonly Action _onTriggerSnip;
    private readonly Action _onExit;

    private bool _isRecording;
    private bool _currentCtrl;
    private bool _currentShift;
    private bool _currentAlt;
    private bool _currentWin;
    private string _currentKey = "S";

    public SettingsWindow(
        SettingsService settingsService,
        OcrService ocrService,
        Action onTriggerSnip,
        Action onExit)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _ocrService = ocrService;
        _onTriggerSnip = onTriggerSnip;
        _onExit = onExit;

        PopulateKeys();
        PopulateLanguages();
        LoadCurrentSettings();
        HookEvents();
        UpdateKeycapsDisplay();
    }

    private void PopulateKeys()
    {
        var keys = new List<string>();
        for (char c = 'A'; c <= 'Z'; c++) keys.Add(c.ToString());
        for (int i = 0; i <= 9; i++) keys.Add(i.ToString());
        for (int i = 1; i <= 12; i++) keys.Add($"F{i}");
        keys.Add("PrintScreen");
        keys.Add("Space");
        keys.Add("Insert");
        keys.Add("Home");

        CmbKey.ItemsSource = keys;
    }

    private void PopulateLanguages()
    {
        var items = new List<string> { "Default (Windows User Profile Languages)" };
        var available = _ocrService.GetAvailableLanguages();
        items.AddRange(available);

        CmbOcrLanguage.ItemsSource = items;
    }

    private void LoadCurrentSettings()
    {
        var s = _settingsService.Settings;

        _currentCtrl = s.HotkeyCtrl;
        _currentShift = s.HotkeyShift;
        _currentAlt = s.HotkeyAlt;
        _currentWin = s.HotkeyWin;
        _currentKey = s.HotkeyKey;

        SyncManualControls();

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
    }

    private void SyncManualControls()
    {
        ChkCtrl.IsChecked = _currentCtrl;
        ChkShift.IsChecked = _currentShift;
        ChkAlt.IsChecked = _currentAlt;
        ChkWin.IsChecked = _currentWin;

        if (CmbKey.Items.Contains(_currentKey))
        {
            CmbKey.SelectedItem = _currentKey;
        }
        else
        {
            CmbKey.SelectedItem = "S";
        }
    }

    private void HookEvents()
    {
        // Recorder button
        BtnRecordShortcut.Click += (s, e) => ToggleRecording();
        PreviewKeyDown += OnWindowPreviewKeyDown;

        // Presets
        PresetCtrlShiftS.Click += (s, e) => ApplyPreset(true, true, false, false, "S");
        PresetAltS.Click += (s, e) => ApplyPreset(false, false, true, false, "S");
        PresetAltC.Click += (s, e) => ApplyPreset(false, false, true, false, "C");
        PresetCtrlAltS.Click += (s, e) => ApplyPreset(true, false, true, false, "S");
        PresetF9.Click += (s, e) => ApplyPreset(false, false, false, false, "F9");

        // Manual controls
        ChkCtrl.Checked += (s, e) => OnManualControlChanged();
        ChkCtrl.Unchecked += (s, e) => OnManualControlChanged();
        ChkShift.Checked += (s, e) => OnManualControlChanged();
        ChkShift.Unchecked += (s, e) => OnManualControlChanged();
        ChkAlt.Checked += (s, e) => OnManualControlChanged();
        ChkAlt.Unchecked += (s, e) => OnManualControlChanged();
        ChkWin.Checked += (s, e) => OnManualControlChanged();
        ChkWin.Unchecked += (s, e) => OnManualControlChanged();
        CmbKey.SelectionChanged += (s, e) => OnManualControlChanged();

        // Footer buttons
        BtnTestSnip.Click += OnTestSnipClicked;
        BtnSave.Click += OnSaveClicked;
        BtnMinimizeToTray.Click += (s, e) => Hide();
        BtnExitApp.Click += (s, e) => _onExit();
    }

    private void ApplyPreset(bool ctrl, bool shift, bool alt, bool win, string key)
    {
        _currentCtrl = ctrl;
        _currentShift = shift;
        _currentAlt = alt;
        _currentWin = win;
        _currentKey = key;

        SyncManualControls();
        UpdateKeycapsDisplay();
    }

    private void OnManualControlChanged()
    {
        if (_isRecording) return;

        _currentCtrl = ChkCtrl.IsChecked == true;
        _currentShift = ChkShift.IsChecked == true;
        _currentAlt = ChkAlt.IsChecked == true;
        _currentWin = ChkWin.IsChecked == true;
        _currentKey = CmbKey.SelectedItem?.ToString() ?? "S";

        UpdateKeycapsDisplay();
    }

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
        TxtRecordIcon.Text = "🔴";
        TxtRecordButton.Text = "Listening... Press keys";
        RecorderBox.BorderBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        Focus();
    }

    private void StopRecording()
    {
        _isRecording = false;
        TxtRecordIcon.Text = "🎙️";
        TxtRecordButton.Text = "Record Shortcut";
        RecorderBox.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecording) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Read active modifiers
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0;

        // Skip pure modifier key presses alone
        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin)
        {
            return;
        }

        // Capture actual key
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

        SyncManualControls();
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
                Background = new SolidColorBrush(Color.FromRgb(37, 42, 61)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(75, 85, 120)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 6, 0)
            };

            var text = new TextBlock
            {
                Text = parts[i],
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253))
            };

            badge.Child = text;
            KeycapsContainer.Children.Add(badge);

            if (i < parts.Count - 1)
            {
                var plus = new TextBlock
                {
                    Text = "+",
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                KeycapsContainer.Children.Add(plus);
            }
        }
    }

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

        MessageBox.Show(
            $"Settings applied!\nNew Global Shortcut: {App.FormatHotkeyString(newSettings)}\n\nPress this shortcut anytime anywhere in Windows to trigger OrbitOCR.",
            "OrbitOCR Settings Saved",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Minimize to tray instead of quitting
        e.Cancel = true;
        Hide();
    }
}
