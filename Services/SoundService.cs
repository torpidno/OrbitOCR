using System.IO;
using System.Media;

namespace OrbitOCR.Services;

public class SoundService
{
    private readonly SettingsService _settingsService;
    private SoundPlayer? _copyChimePlayer;

    public SoundService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeChimePlayer();
    }

    private void InitializeChimePlayer()
    {
        try
        {
            // Generate a subtle, pleasant 80ms harmonic chime (C6 - 1046.5Hz) in memory
            var wavBytes = GeneratePleasantToneWav(1046.5, 80, 0.25);
            _copyChimePlayer = new SoundPlayer(new MemoryStream(wavBytes));
            _copyChimePlayer.Load();
        }
        catch
        {
            _copyChimePlayer = null;
        }
    }

    public void PlayCopySuccess()
    {
        if (!_settingsService.Settings.PlaySounds) return;

        try
        {
            if (_copyChimePlayer != null)
            {
                _copyChimePlayer.Play();
            }
            else
            {
                SystemSounds.Asterisk.Play();
            }
        }
        catch
        {
            // Ignore sound errors
        }
    }

    public void PlaySnipTrigger()
    {
        // Quiet or soft feedback if needed
    }

    private static byte[] GeneratePleasantToneWav(double frequency, int durationMs, double volume)
    {
        int sampleRate = 44100;
        int numSamples = (int)(sampleRate * (durationMs / 1000.0));
        short[] samples = new short[numSamples];

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            // Smooth attack and release envelope to eliminate any clicks
            double progress = (double)i / numSamples;
            double envelope = Math.Sin(progress * Math.PI); // Half sine curve envelope

            // Primary tone + gentle overtone
            double wave = Math.Sin(2.0 * Math.PI * frequency * t) * 0.8
                        + Math.Sin(2.0 * Math.PI * (frequency * 1.5) * t) * 0.2;

            samples[i] = (short)(wave * envelope * volume * short.MaxValue);
        }

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RIFF header
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + numSamples * 2);
        writer.Write("WAVE"u8.ToArray());

        // Subchunk 1 "fmt "
        writer.Write("fmt "u8.ToArray());
        writer.Write(16); // Subchunk1Size for PCM
        writer.Write((short)1); // AudioFormat = 1 (PCM)
        writer.Write((short)1); // NumChannels = 1 (Mono)
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2); // ByteRate
        writer.Write((short)2); // BlockAlign
        writer.Write((short)16); // BitsPerSample

        // Subchunk 2 "data"
        writer.Write("data"u8.ToArray());
        writer.Write(numSamples * 2);

        for (int i = 0; i < numSamples; i++)
        {
            writer.Write(samples[i]);
        }

        return ms.ToArray();
    }
}
