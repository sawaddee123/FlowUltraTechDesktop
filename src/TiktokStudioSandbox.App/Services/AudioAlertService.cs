using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace TiktokStudioSandbox.App.Services;

public static class AudioAlertService
{
    private static MediaPlayer? _mediaPlayer;
    private static bool _isLooping = false;
    private static readonly string DefaultWavPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "captcha_alert.wav");

    public static bool IsAlerting => _isLooping;

    public static void PlayAlert(int volumePercent = 80, string? customFilePath = null, bool loop = false)
    {
        try
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => PlayAlertInternal(volumePercent, customFilePath, loop));
            }
            else
            {
                PlayAlertInternal(volumePercent, customFilePath, loop);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioAlertService] PlayAlert error: {ex.Message}");
            FallbackSystemSound();
        }
    }

    public static void StopAlert()
    {
        try
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(StopAlertInternal);
            }
            else
            {
                StopAlertInternal();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioAlertService] StopAlert error: {ex.Message}");
        }
    }

    private static void StopAlertInternal()
    {
        _isLooping = false;
        try
        {
            _mediaPlayer?.Stop();
        }
        catch { }
    }

    private static void PlayAlertInternal(int volumePercent, string? customFilePath, bool loop)
    {
        try
        {
            string? pathToPlay = null;

            if (!string.IsNullOrWhiteSpace(customFilePath) && File.Exists(customFilePath))
            {
                pathToPlay = customFilePath;
            }
            else
            {
                EnsureDefaultAlertWav(DefaultWavPath);
                if (File.Exists(DefaultWavPath))
                {
                    pathToPlay = DefaultWavPath;
                }
            }

            if (pathToPlay != null)
            {
                if (_mediaPlayer == null)
                {
                    _mediaPlayer = new MediaPlayer();
                    _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
                }

                _isLooping = loop;
                _mediaPlayer.Stop();
                _mediaPlayer.Open(new Uri(pathToPlay, UriKind.Absolute));
                _mediaPlayer.Volume = Math.Clamp(volumePercent / 100.0, 0.0, 1.0);
                _mediaPlayer.Play();
            }
            else
            {
                FallbackSystemSound();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioAlertService] PlayAlertInternal failed: {ex.Message}");
            FallbackSystemSound();
        }
    }

    private static void MediaPlayer_MediaEnded(object? sender, EventArgs e)
    {
        if (_isLooping && _mediaPlayer != null)
        {
            _mediaPlayer.Position = TimeSpan.Zero;
            _mediaPlayer.Play();
        }
    }

    private static void FallbackSystemSound()
    {
        try
        {
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch { }
    }

    private static void EnsureDefaultAlertWav(string wavPath)
    {
        if (File.Exists(wavPath)) return;

        try
        {
            string? dir = Path.GetDirectoryName(wavPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            const int sampleRate = 44100;
            const double duration = 0.65;
            int numSamples = (int)(sampleRate * duration);
            short[] samples = new short[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                double t = (double)i / sampleRate;
                // Two-tone chime chord: 880Hz (A5) + 1318.5Hz (E6)
                double wave1 = Math.Sin(2 * Math.PI * 880.0 * t);
                double wave2 = Math.Sin(2 * Math.PI * 1318.5 * t);
                double envelope = Math.Exp(-5.5 * t); // Natural exponential decay
                double sampleValue = (wave1 * 0.6 + wave2 * 0.4) * envelope * 32767.0 * 0.90;
                samples[i] = (short)Math.Clamp(sampleValue, -32768, 32767);
            }

            using var fs = new FileStream(wavPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // RIFF header
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + numSamples * 2);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt subchunk
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // Subchunk1Size (16 for PCM)
            bw.Write((short)1); // AudioFormat (1 for PCM)
            bw.Write((short)1); // NumChannels (1 for mono)
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // ByteRate
            bw.Write((short)2); // BlockAlign
            bw.Write((short)16); // BitsPerSample

            // data subchunk
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(numSamples * 2);
            for (int i = 0; i < numSamples; i++)
            {
                bw.Write(samples[i]);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioAlertService] Failed to create default wav: {ex.Message}");
        }
    }
}
