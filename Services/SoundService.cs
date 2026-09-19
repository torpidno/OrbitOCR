using System.Media;

namespace OrbitOCR.Services;

public class SoundService
{
    private readonly SettingsService _settingsService;

    public SoundService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void PlayCopySuccess()
    {
        if (!_settingsService.Settings.PlaySounds) return;

        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Ignore sound errors
        }
    }
}
