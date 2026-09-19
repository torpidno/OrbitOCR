namespace OrbitOCR.Models;

public class AppSettings
{
    public bool HotkeyCtrl { get; set; } = true;
    public bool HotkeyShift { get; set; } = true;
    public bool HotkeyAlt { get; set; } = false;
    public bool HotkeyWin { get; set; } = false;
    public string HotkeyKey { get; set; } = "S";

    public bool AutoCopyOnSnip { get; set; } = false;
    public bool PlaySounds { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public string? PreferredOcrLanguage { get; set; } = null; // null = use Windows user profile languages
}
