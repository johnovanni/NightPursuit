using NAudio.Wave;
using NightPursuit.Core.Audio;

namespace NightPursuit.Core.Tests.Audio;

/// <summary>Behaviour of the mixer: one-off cues, volumes, the voice limit, music and the limiter.</summary>
public class AudioMixerTests : IDisposable
{
    private const int Rate = AudioFormat.SampleRate;
    private const float Ceiling = 0.891f;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "np-mixer-" + Guid.NewGuid().ToString("N"));

    public AudioMixerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A mono 16-bit cue file holding a constant level.</summary>
    private void WriteDc(SoundCue cue, float level, double seconds)
    {
        var samples = new float[(int)(seconds * Rate)];
        Array.Fill(samples, level);
        SoundFiles.WriteMono16(Path.Combine(_dir, SoundCues.FileName(cue)), samples);
    }

    private AudioMixer MakeMixer(params (SoundCue Cue, float Level, double Seconds)[] cues)
    {
        foreach (var c in cues) WriteDc(c.Cue, c.Level, c.Seconds);
        var bank = SoundBank.Load(_dir, out _);
        var mixer = new AudioMixer(bank) { SoundOn = true, MusicVolume = 7, EffectsVolume = 10 };
        Read(mixer, 0.1); // let the volume ramps settle
        return mixer;
    }

    /// <summary>Reads stereo frames and returns the interleaved samples.</summary>
    private static float[] Read(ISampleProvider p, double seconds)
    {
        var buffer = new float[(int)(seconds * Rate) * 2];
        int done = 0;
        while (done < buffer.Length)
        {
            int n = Math.Min(1024, buffer.Length - done);
            int got = p.Read(buffer.AsSpan(done, n));
            Assert.Equal(n, got);
            done += n;
        }
        return buffer;
    }

    private static float Left(float[] b, int frame) => b[2 * frame];
    private static float Right(float[] b, int frame) => b[2 * frame + 1];

    private sealed class Constant(float value) : ISampleProvider
    {
        public int Reads;
        public WaveFormat WaveFormat => AudioFormat.Stereo;

        public int Read(Span<float> buffer)
        {
            Interlocked.Increment(ref Reads);
            buffer.Fill(value);
            return buffer.Length;
        }
    }

    [Fact]
    public void A_cue_plays_its_samples_then_ends()
    {
        var mixer = MakeMixer((SoundCue.Pickup, 0.4f, 0.1));
        mixer.Play(new CuePlay(SoundCue.Pickup, 1, -1));
        Assert.Equal(1, mixer.ActiveVoices);
        var b = Read(mixer, 0.2);
        int loud = 0;
        for (int i = 0; i < b.Length / 2; i++)
            if (Math.Abs(Left(b, i) - 0.4f) < 0.01f) loud++;
        Assert.InRange(loud, 4300, 4420);            // 0.1 s at 44,100 Hz is 4,410 frames
        Assert.Equal(0f, Left(b, b.Length / 2 - 1));  // ended, and silent afterwards
        Assert.Equal(0, mixer.ActiveVoices);
    }

    [Fact]
    public void Pan_left_puts_the_sound_in_the_left_channel()
    {
        var mixer = MakeMixer((SoundCue.Pickup, 0.4f, 0.1));
        mixer.Play(new CuePlay(SoundCue.Pickup, 1, -1));
        var left = Read(mixer, 0.05);
        Assert.True(Left(left, 1000) > 0.3f);
        Assert.Equal(0f, Right(left, 1000), 1e-4f);

        Read(mixer, 0.1); // let the first finish
        mixer.Play(new CuePlay(SoundCue.Pickup, 1, 1));
        var right = Read(mixer, 0.05);
        Assert.True(Right(right, 1000) > 0.3f);
        Assert.Equal(0f, Left(right, 1000), 1e-4f);
    }

    private static float Plateau(AudioMixer mixer, int effects)
    {
        mixer.EffectsVolume = effects;
        Read(mixer, 0.1);
        mixer.Play(new CuePlay(SoundCue.Pickup, 1, -1));
        var b = Read(mixer, 0.2);
        return Left(b, 4000);
    }

    [Fact]
    public void Effects_volume_0_is_silent_and_10_is_full()
    {
        var mixer = MakeMixer((SoundCue.Pickup, 0.4f, 0.2));
        mixer.EffectsVolume = 0;
        Read(mixer, 0.1);
        mixer.Play(new CuePlay(SoundCue.Pickup, 1, -1));
        Assert.Equal(0f, Read(mixer, 0.1).Max(Math.Abs), 1e-6f);

        var full = MakeMixer((SoundCue.Pickup, 0.4f, 0.2));
        Assert.Equal(0.4f, Plateau(full, 10), 0.002f);
    }

    [Fact]
    public void Volume_steps_are_3_dB()
    {
        for (int v = 1; v <= 10; v++)
        {
            var mixer = MakeMixer((SoundCue.Pickup, 0.4f, 0.2));
            float level = Plateau(mixer, v);
            double expectedDb = (v - 10) * 3.0;
            double db = 20 * Math.Log10(level / 0.4);
            Assert.Equal(expectedDb, db, 0.1);
            Assert.Equal(Math.Pow(10, expectedDb / 20), AudioMixer.Gain(v), 1e-9);
        }
        Assert.Equal(0.0, AudioMixer.Gain(0));
        Assert.Equal(1.0, AudioMixer.Gain(10));
    }

    [Fact]
    public void A_volume_change_ramps_without_a_click()
    {
        var mixer = MakeMixer((SoundCue.Pickup, 0.4f, 1.0));
        mixer.Play(new CuePlay(SoundCue.Pickup, 1, -1));
        Read(mixer, 0.1);
        mixer.EffectsVolume = 4;
        var b = Read(mixer, 0.2);
        float largestStep = 0;
        for (int i = 1; i < b.Length / 2; i++)
            largestStep = Math.Max(largestStep, Math.Abs(Left(b, i) - Left(b, i - 1)));
        Assert.True(largestStep < 0.01f, $"largest step was {largestStep}");
        Assert.Equal(0.4f * (float)AudioMixer.Gain(4), Left(b, b.Length / 2 - 1), 0.003f);
    }

    [Fact]
    public void Sound_off_writes_silence()
    {
        var mixer = MakeMixer((SoundCue.Pickup, 0.4f, 1.0));
        mixer.Play(new CuePlay(SoundCue.Pickup, 1, -1));
        mixer.SetMusic(new Constant(0.3f), 0);
        Read(mixer, 0.1);
        mixer.SoundOn = false;
        Read(mixer, 0.05); // the 20 ms ramp
        Assert.All(Read(mixer, 0.1), s => Assert.Equal(0f, s));
        Assert.Equal(1, mixer.ActiveVoices); // still running underneath

        mixer.SoundOn = true;
        Read(mixer, 0.05);
        var back = Read(mixer, 0.1);
        float music = 0.3f * (float)AudioMixer.Gain(7);
        Assert.Equal(0.4f + music, Left(back, 100), 0.01f); // the cue carried on; it did not restart or vanish
    }

    [Fact]
    public void The_25th_voice_drops_the_quietest()
    {
        var mixer = MakeMixer((SoundCue.Pickup, 0.1f, 1.0));
        mixer.Play(new CuePlay(SoundCue.Pickup, 0.2, -1)); // the quiet one, left
        for (int i = 0; i < 23; i++) mixer.Play(new CuePlay(SoundCue.Pickup, 0.8, 1));
        Assert.Equal(24, mixer.ActiveVoices);
        mixer.Play(new CuePlay(SoundCue.Pickup, 0.8, 1)); // the 25th pushes the quiet one out
        Assert.Equal(24, mixer.ActiveVoices);
        var b = Read(mixer, 0.05);
        Assert.Equal(0f, Left(b, 2000), 1e-4f);

        // A newcomer quieter than all of them is the one dropped.
        mixer.Play(new CuePlay(SoundCue.Pickup, 0.01, -1));
        Assert.Equal(24, mixer.ActiveVoices);
        Assert.Equal(0f, Left(Read(mixer, 0.05), 2000), 1e-4f);
    }

    [Fact]
    public void A_burst_of_fifty_explosions_never_passes_the_ceiling()
    {
        var mixer = MakeMixer((SoundCue.Explosion, 0.7f, 0.5));
        for (int i = 0; i < 50; i++) mixer.Play(new CuePlay(SoundCue.Explosion, 1, i % 2 == 0 ? -1 : 1));
        Assert.Equal(24, mixer.ActiveVoices);
        var b = Read(mixer, 0.6);
        float peak = b.Max(Math.Abs);
        Assert.True(peak <= Ceiling, $"peak {peak}");
        Assert.True(peak > 0.6f, $"peak {peak} - the burst should be loud");
    }

    [Fact]
    public void Music_cross_fade_takes_the_time_given_and_ends_on_the_new_track()
    {
        var mixer = MakeMixer();
        var oldTrack = new Constant(0.3f);
        var newTrack = new Constant(0.6f);
        float g = (float)AudioMixer.Gain(7);
        mixer.SetMusic(oldTrack, 0);
        Read(mixer, 0.1);
        Assert.Equal(0.3f * g, Left(Read(mixer, 0.05), 500), 0.003f);

        mixer.SetMusic(newTrack, 1.0);
        var b = Read(mixer, 1.2);
        Assert.Equal(0.45f * g, Left(b, Rate / 2), 0.01f);        // half way: half of each
        Assert.True(Left(b, (int)(0.9 * Rate)) < 0.6f * g - 0.01f); // not there yet at 0.9 s
        Assert.Equal(0.6f * g, Left(b, (int)(1.05 * Rate)), 0.003f);
        Assert.Equal(0.6f * g, Left(b, (int)(1.15 * Rate)), 0.003f);

        int reads = oldTrack.Reads;
        Read(mixer, 0.1);
        Assert.Equal(reads, oldTrack.Reads); // the old track was dropped once its fade ended
    }

    [Fact]
    public void A_second_music_change_mid_fade_starts_from_the_current_level_without_a_step()
    {
        var mixer = MakeMixer();
        mixer.SetMusic(new Constant(0.5f), 0);
        mixer.SetMusic(new Constant(0.5f), 1.0);
        Read(mixer, 0.1);
        mixer.SetMusic(new Constant(0.5f), 1.0);
        var b = Read(mixer, 0.6);
        mixer.SetMusic(new Constant(0.5f), 1.0);
        var c = Read(mixer, 1.5);
        float largestStep = 0;
        foreach (var x in new[] { b, c })
            for (int i = 1; i < x.Length / 2; i++)
                largestStep = Math.Max(largestStep, Math.Abs(Left(x, i) - Left(x, i - 1)));
        Assert.True(largestStep < 0.002f, $"largest step was {largestStep}");
    }

    [Fact]
    public void Many_music_changes_without_reading_do_not_pile_up_or_throw()
    {
        var mixer = MakeMixer();
        for (int i = 0; i < 2000; i++) mixer.SetMusic(new Constant(0.1f), 1.0);
        Read(mixer, 0.1);
    }

    [Fact]
    public void Music_set_to_null_fades_to_silence()
    {
        var mixer = MakeMixer();
        mixer.SetMusic(new Constant(0.5f), 0);
        Read(mixer, 0.1);
        mixer.SetMusic(null, 0.2);
        var b = Read(mixer, 0.4);
        Assert.Equal(0f, Left(b, b.Length / 2 - 1), 1e-5f);
        Assert.True(Left(b, 3000) > 0.01f); // still fading at 70 ms
    }

    [Fact]
    public void Paused_music_level_ramps_to_40_percent()
    {
        var mixer = MakeMixer();
        float g = (float)AudioMixer.Gain(7);
        Assert.Equal(1.0, mixer.MusicLevel);
        mixer.SetMusic(new Constant(0.5f), 0);
        Read(mixer, 0.1);
        Assert.Equal(0.5f * g, Left(Read(mixer, 0.05), 100), 0.003f);

        mixer.MusicLevel = 0.4;
        var b = Read(mixer, 0.5);
        float mid = Left(b, (int)(0.15 * Rate));
        Assert.InRange(mid, 0.5f * g * 0.4f + 0.01f, 0.5f * g - 0.01f); // part way at 0.15 s
        Assert.Equal(0.5f * g * 0.4f, Left(b, (int)(0.4 * Rate)), 0.003f);
        float largestStep = 0;
        for (int i = 1; i < b.Length / 2; i++)
            largestStep = Math.Max(largestStep, Math.Abs(Left(b, i) - Left(b, i - 1)));
        Assert.True(largestStep < 0.001f, $"largest step was {largestStep}");
    }

    [Fact]
    public void A_missing_sound_file_is_reported_and_silent()
    {
        WriteDc(SoundCue.Pickup, 0.4f, 0.1);
        File.WriteAllText(Path.Combine(_dir, SoundCues.FileName(SoundCue.Warning)), "this is not a wav file");
        var bank = SoundBank.Load(_dir, out var problems);
        Assert.NotNull(bank.Samples(SoundCue.Pickup));
        Assert.Null(bank.Samples(SoundCue.GunPlayer));
        Assert.Null(bank.Samples(SoundCue.Warning));
        Assert.Contains(problems, p => p.Contains(SoundCues.FileName(SoundCue.GunPlayer)));
        Assert.Contains(problems, p => p.Contains(SoundCues.FileName(SoundCue.Warning)));
        Assert.Equal(Enum.GetValues<SoundCue>().Length - 1, problems.Count);

        var mixer = new AudioMixer(bank) { EffectsVolume = 10 };
        mixer.Play(new CuePlay(SoundCue.GunPlayer));
        Assert.Equal(0, mixer.ActiveVoices);
        Assert.All(Read(mixer, 0.05), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void A_missing_folder_reports_every_cue_and_does_not_throw()
    {
        var bank = SoundBank.Load(Path.Combine(_dir, "nowhere"), out var problems);
        Assert.Equal(Enum.GetValues<SoundCue>().Length, problems.Count);
        Assert.Null(bank.Samples(SoundCue.Pickup));
    }

    [Fact]
    public void The_shipped_sound_files_all_load()
    {
        var bank = SoundBank.Load(Path.Combine(AppContext.BaseDirectory, "content", "sounds"), out var problems);
        Assert.Empty(problems);
        Assert.All(Enum.GetValues<SoundCue>(), c => Assert.NotEmpty(bank.Samples(c)!));
    }

    [Fact]
    public void An_input_added_to_the_effects_bus_is_heard_and_follows_the_effects_volume()
    {
        var mixer = MakeMixer();
        mixer.AddEffectsInput(new Constant(0.4f));
        Read(mixer, 0.1);
        Assert.Equal(0.4f, Left(Read(mixer, 0.05), 500), 0.003f);
        mixer.EffectsVolume = 7;
        Read(mixer, 0.1);
        Assert.Equal(0.4f * (float)AudioMixer.Gain(7), Left(Read(mixer, 0.05), 500), 0.003f);
        mixer.EffectsVolume = 0;
        Read(mixer, 0.1);
        Assert.Equal(0f, Left(Read(mixer, 0.05), 500), 1e-5f);
    }

    [Fact]
    public void Play_from_another_thread_while_reading_does_not_throw()
    {
        var mixer = MakeMixer((SoundCue.Pickup, 0.3f, 0.05), (SoundCue.Explosion, 0.5f, 0.3));
        mixer.SetMusic(new Constant(0.2f), 0.01);
        using var stop = new CancellationTokenSource();
        Exception? failure = null;
        var player = new Thread(() =>
        {
            try
            {
                var rng = new Random(1);
                int count = 0;
                while (!stop.IsCancellationRequested)
                {
                    mixer.Play(new CuePlay(rng.Next(2) == 0 ? SoundCue.Pickup : SoundCue.Explosion, rng.NextDouble(), rng.NextDouble() * 2 - 1));
                    mixer.EffectsVolume = rng.Next(11);
                    if (++count % 20 == 0) mixer.SetMusic(new Constant(0.2f), 0.005);
                    mixer.MusicLevel = rng.Next(2) == 0 ? 1.0 : 0.4;
                    mixer.SoundOn = rng.Next(8) != 0;
                    Thread.Sleep(1);
                }
            }
            catch (Exception e) { failure = e; }
        });
        player.Start();
        var buffer = new float[1024];
        for (int i = 0; i < 1500; i++)
        {
            Assert.Equal(buffer.Length, mixer.Read(buffer));
            Assert.All(buffer, s => Assert.InRange(s, -Ceiling, Ceiling));
        }
        stop.Cancel();
        player.Join();
        Assert.Null(failure);
    }
}
