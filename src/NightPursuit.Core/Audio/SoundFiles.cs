using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NightPursuit.Core.Audio;

/// <summary>Reads and writes the game's effect WAV files: 44,100 Hz, 16-bit PCM.</summary>
public static class SoundFiles
{
    /// <summary>
    /// Loads a 16-bit PCM, 44,100 Hz WAV, mono or stereo (stereo is averaged to mono).
    /// Anything else throws <see cref="InvalidDataException"/>.
    /// </summary>
    public static float[] LoadMono(string path)
    {
        using var reader = new WaveFileReader(path);
        var f = reader.WaveFormat;
        if (f.Encoding != WaveFormatEncoding.Pcm || f.BitsPerSample != 16)
            throw new InvalidDataException(
                $"{Path.GetFileName(path)}: only 16-bit PCM WAV files can be used (this one is {f.BitsPerSample}-bit {f.Encoding}).");
        if (f.SampleRate != AudioFormat.SampleRate)
            throw new InvalidDataException(
                $"{Path.GetFileName(path)}: the sample rate must be 44,100 Hz (this one is {f.SampleRate:N0} Hz).");
        if (f.Channels is not (1 or 2))
            throw new InvalidDataException(
                $"{Path.GetFileName(path)}: the file must be mono or stereo (this one has {f.Channels} channels).");

        var bytes = new byte[reader.Length];
        reader.ReadExactly(bytes);
        int channels = f.Channels;
        int frames = bytes.Length / (2 * channels);
        var result = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
                sum += BitConverter.ToInt16(bytes, (i * channels + c) * 2) / 32768f;
            result[i] = sum / channels;
        }
        return result;
    }

    /// <summary>
    /// Loads any ordinary PCM or float WAV (any sample rate, 8/16/24/32-bit, mono or stereo) as 44,100 Hz mono floats:
    /// stereo is averaged, and a different sample rate is resampled. Problems throw <see cref="InvalidDataException"/>
    /// with a plain message that does not repeat the file name. This is what the sound bank uses, so a WAV exported from
    /// any editor works.
    /// </summary>
    public static float[] LoadConvertedMono(string path)
    {
        using var reader = new WaveFileReader(path);
        var f = reader.WaveFormat;
        if (f.Channels is not (1 or 2))
            throw new InvalidDataException($"the file must be mono or stereo (this one has {f.Channels} channels)");
        if (f.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible))
            throw new InvalidDataException($"only normal PCM or float WAV files can be used (this one is {f.Encoding})");

        ISampleProvider source;
        try { source = reader.ToSampleProvider(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException($"this kind of WAV cannot be used (it is {f.BitsPerSample}-bit {f.Encoding}); export it as 16-bit PCM");
        }
        if (f.Channels == 2) source = new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (f.SampleRate != AudioFormat.SampleRate) source = new WdlResamplingSampleProvider(source, AudioFormat.SampleRate);

        var result = new List<float>();
        var block = new float[4096];
        int n;
        while ((n = source.Read(block)) > 0)
            for (int i = 0; i < n; i++) result.Add(block[i]);
        return result.ToArray();
    }

    /// <summary>Writes mono samples as a 16-bit PCM, 44,100 Hz WAV.</summary>
    public static void WriteMono16(string path, ReadOnlySpan<float> samples) => Write(path, samples, 1);

    /// <summary>Writes interleaved left/right samples as a 16-bit PCM, 44,100 Hz stereo WAV.</summary>
    public static void WriteStereo16(string path, ReadOnlySpan<float> interleaved) => Write(path, interleaved, 2);

    private static void Write(string path, ReadOnlySpan<float> samples, int channels)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(AudioFormat.SampleRate, 16, channels));
        var array = samples.ToArray();
        writer.WriteSamples(array, 0, array.Length);
    }
}
