using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NightPursuit.Core.Audio;

namespace NightPursuit.App.Audio;

/// <summary>
/// One music file as 44.1 kHz stereo float that loops from the start and never ends. NAudio's
/// <see cref="AudioFileReader"/> decodes WAV and MP3; a mono file is widened and another rate resampled. The audio
/// thread reads while the game thread may dispose, so both go through one lock (a read after Dispose gives silence).
/// </summary>
public sealed class MusicDecoder : ISampleProvider, IDisposable
{
    readonly AudioFileReader _reader;
    readonly ISampleProvider _chain;
    readonly Lock _lock = new();
    bool _disposed;

    MusicDecoder(AudioFileReader reader, ISampleProvider chain)
    {
        _reader = reader;
        _chain = chain;
    }

    public WaveFormat WaveFormat => AudioFormat.Stereo;

    /// <summary>Opens <paramref name="path"/> at <paramref name="volume"/> (a track's own gain), or returns null and sets the notice text.</summary>
    public static MusicDecoder? TryOpen(string path, double volume, out string? problem)
    {
        string name = Path.GetFileName(path).ToUpperInvariant();
        if (!File.Exists(path))
        {
            problem = $"MUSIC FILE NOT FOUND: {name}";
            return null;
        }
        AudioFileReader? reader = null;
        try
        {
            reader = new AudioFileReader(path) { Volume = (float)volume };
            ISampleProvider chain = reader;
            int channels = reader.WaveFormat.Channels;
            if (channels == 1) chain = new MonoToStereoSampleProvider(chain);
            else if (channels != 2) throw new InvalidDataException("only mono and stereo files are supported");
            if (reader.WaveFormat.SampleRate != AudioFormat.SampleRate) chain = new WdlResamplingSampleProvider(chain, AudioFormat.SampleRate);
            problem = null;
            return new MusicDecoder(reader, chain);
        }
        catch (Exception)
        {
            try { reader?.Dispose(); } catch { /* already failing */ }
            problem = $"MUSIC FILE COULD NOT BE READ: {name}";
            return null;
        }
    }

    /// <summary>Always fills the buffer: at the end of the file it goes back to the start.</summary>
    public int Read(Span<float> buffer)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                buffer.Clear();
                return buffer.Length;
            }
            try
            {
                int filled = 0;
                bool gotAny = true;   // anything since the last rewind; false twice running means an empty file
                while (filled < buffer.Length)
                {
                    int n = _chain.Read(buffer[filled..]);
                    if (n > 0) { filled += n; gotAny = true; continue; }
                    if (!gotAny) break;
                    gotAny = false;
                    _reader.Position = 0;
                }
                buffer[filled..].Clear();
            }
            catch (Exception)
            {
                buffer.Clear();   // a failing decode is silence, never a crash on the audio thread
            }
            return buffer.Length;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _reader.Dispose(); } catch { /* closing */ }
        }
    }
}
