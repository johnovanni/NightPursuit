using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NightPursuit.Core.Audio;

namespace NightPursuit.App.Audio;

/// <summary>
/// Plays the mixer through the default sound device (NAudio.WinMM's <see cref="WaveOut"/>, which NAudio 3.x renamed
/// from WaveOutEvent). NAudio pulls samples on its own thread. If the device goes away the output stops quietly and
/// <see cref="Lost"/> becomes true: there is no reconnect (restarting the game brings sound back).
/// </summary>
public sealed class AudioOutput : IDisposable
{
    /// <summary>Total latency about 80 ms: three buffers of 27 ms.</summary>
    const int BufferMilliseconds = 27, Buffers = 3;

    readonly WaveOut _device;
    volatile bool _lost;
    bool _disposed;

    AudioOutput(WaveOut device) => _device = device;

    /// <summary>True once the device stopped with an error. The game checks it each frame and shows a notice once.</summary>
    public bool Lost => _lost;

    /// <summary>Starts playing <paramref name="mixer"/>, or returns null with the reason when there is no device or it will not start.</summary>
    public static AudioOutput? TryStart(AudioMixer mixer, out string? problem)
    {
        WaveOut? device = null;
        try
        {
            if (WaveOut.DeviceCount == 0)
            {
                problem = "SOUND IS OFF: NO SOUND DEVICE FOUND";
                return null;
            }
            device = new WaveOut { BufferMilliseconds = BufferMilliseconds, NumberOfBuffers = Buffers };
            var output = new AudioOutput(device);
            device.PlaybackStopped += output.OnStopped;
            device.Init(new SampleToWaveProvider(mixer));
            device.Play();
            problem = null;
            return output;
        }
        catch (Exception ex)
        {
            try { device?.Dispose(); } catch { /* nothing more to do */ }
            problem = "SOUND IS OFF: " + Reason(ex);
            return null;
        }
    }

    void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null && !_disposed) _lost = true;
    }

    // A short upper-case reason that fits a one-line notice.
    static string Reason(Exception ex)
    {
        string text = ex.Message.Split('\n')[0].Trim().TrimEnd('.').ToUpperInvariant();
        return text.Length == 0 ? "THE SOUND DEVICE WOULD NOT START" : text.Length > 60 ? text[..60] : text;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _device.Stop(); } catch { /* closing */ }
        try { _device.Dispose(); } catch { /* closing */ }
    }
}
