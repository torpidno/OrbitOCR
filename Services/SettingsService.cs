using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using OrbitOCR.Models;

namespace OrbitOCR.Services;

public class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OrbitOCR");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");
    private const string StartupRegistryKeyName = "OrbitOCR";

    public AppSettings Settings { get; private set; }

    public event Action<AppSettings>? SettingsChanged;

    public SettingsService()
    {
        Settings = LoadSettings();
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Fallback to default if corrupt or read error
        }

        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        try
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore file write errors
        }

        ApplyStartupRegistry(settings.StartWithWindows);
        SettingsChanged?.Invoke(Settings);
    }

    public void ApplyStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            if (enable)
            {
                key.SetValue(StartupRegistryKeyName, $"\"{exePath}\" --minimized");
            }
            else
            {
                if (key.GetValue(StartupRegistryKeyName) != null)
                {
                    key.DeleteValue(StartupRegistryKeyName, false);
                }
            }
        }
        catch
        {
            // Non-critical if user permissions deny registry write
        }
    }
}
