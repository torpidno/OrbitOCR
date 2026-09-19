using System.Windows;
using System.Windows.Controls;
using OrbitOCR.Models;
using OrbitOCR.Services;

namespace OrbitOCR.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly OcrService _ocrService;

    public SettingsWindow(SettingsService settingsService, OcrService ocrService)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _ocrService = ocrService;

        PopulateKeys();
        PopulateLanguages();
        LoadCurrentSettings();
        HookEvents();
        UpdateHotkeyPreview();
    }

    private void PopulateKeys()
    {
        // Common keys suitable for snip hotkeys
        var keys = new List<string>();
        for (char c = 'A'; c <= 'Z'; c++) keys.Add(c.ToString());
        for (int i = 0; i <= 9; i++) keys.Add(i.ToString());
        for (int i = 1; i <= 12; i++) keys.Add($"F{i}");
        keys.Add("PrintScreen");
        keys.Add("Space");

        CmbKey.ItemsSource = keys;
    }

    private void PopulateLanguages()
    {
        var items = new List<string> { "Default (User Profile Languages)" };
        var available = _ocrService.GetAvailableLanguages();
        items.AddRange(available);

        CmbOcrLanguage.ItemsSource = items;
    }

    private void LoadCurrentSettings()
    {
        var s = _settingsService.Settings;

        ChkCtrl.IsChecked = s.HotkeyCtrl;
        ChkShift.IsChecked = s.HotkeyShift;
        ChkAlt.IsChecked = s.HotkeyAlt;
        ChkWin.IsChecked = s.HotkeyWin;

        if (CmbKey.Items.Contains(s.HotkeyKey))
        {
            CmbKey.SelectedItem = s.HotkeyKey;
        }
        else
        {
            CmbKey.SelectedItem = "S";
        }

        if (s.DefaultSelectionMode == SnipSelectionMode.Lasso)
        {
            RadioModeLasso.IsChecked = true;
        }
        else
        {
            RadioModeRect.IsChecked = true;
        }

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

    private void HookEvents()
    {
        ChkCtrl.Checked += (s, e) => UpdateHotkeyPreview();
        ChkCtrl.Unchecked += (s, e) => UpdateHotkeyPreview();
        ChkShift.Checked += (s, e) => UpdateHotkeyPreview();
        ChkShift.Unchecked += (s, e) => UpdateHotkeyPreview();
        ChkAlt.Checked += (s, e) => UpdateHotkeyPreview();
        ChkAlt.Unchecked += (s, e) => UpdateHotkeyPreview();
        ChkWin.Checked += (s, e) => UpdateHotkeyPreview();
        ChkWin.Unchecked += (s, e) => UpdateHotkeyPreview();

        CmbKey.SelectionChanged += (s, e) => UpdateHotkeyPreview();

        BtnSave.Click += OnSaveClicked;
        BtnCancel.Click += (s, e) => Close();
    }

    private void UpdateHotkeyPreview()
    {
        var parts = new List<string>();
        if (ChkCtrl.IsChecked == true) parts.Add("Ctrl");
        if (ChkShift.IsChecked == true) parts.Add("Shift");
        if (ChkAlt.IsChecked == true) parts.Add("Alt");
        if (ChkWin.IsChecked == true) parts.Add("Win");

        var key = CmbKey.SelectedItem?.ToString() ?? "S";
        parts.Add(key);

        TxtHotkeyPreview.Text = string.Join(" + ", parts);
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (ChkCtrl.IsChecked != true && ChkShift.IsChecked != true &&
            ChkAlt.IsChecked != true && ChkWin.IsChecked != true)
        {
            MessageBox.Show(
                "Please select at least one modifier key (Ctrl, Shift, Alt, or Windows) for the global hotkey.",
                "Invalid Hotkey",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var newSettings = new AppSettings
        {
            HotkeyCtrl = ChkCtrl.IsChecked == true,
            HotkeyShift = ChkShift.IsChecked == true,
            HotkeyAlt = ChkAlt.IsChecked == true,
            HotkeyWin = ChkWin.IsChecked == true,
            HotkeyKey = CmbKey.SelectedItem?.ToString() ?? "S",
            DefaultSelectionMode = RadioModeLasso.IsChecked == true ? SnipSelectionMode.Lasso : SnipSelectionMode.Rectangle,
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

        Close();
    }
}
