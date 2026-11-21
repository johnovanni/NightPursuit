using NightPursuit.Core.Audio;
using static SynthSounds.Dsp;
using static NightPursuit.Core.Audio.OscillatorWave;

namespace SynthSounds;

/// <summary>
/// One recipe per <see cref="SoundCue"/>, in the 80s night-drive synth style: saw, square and sine oscillators,
/// filtered noise, pitch sweeps, punchy envelopes, bass-led, with short gated reverb on hits and chorus on stabs.
/// Only Warning and MissileAlarm use bright tones (fundamental over 1.5 kHz). Every noise source has a fixed seed
/// (the cue number times 100, plus a small offset), so a re-run gives identical files.
/// </summary>
internal static class Recipes
{
    /// <summary>Renders one cue: fades its ends and normalises its peak to -3 dBFS.</summary>
    public static float[] Render(SoundCue cue)
    {
        int seed = ((int)cue + 1) * 100;
        return Finish(Raw(cue, seed));
    }

    private static float[] Raw(SoundCue cue, int seed) => cue switch
    {
        // ---- weapons ----
        // Short punchy saw thunk dropping from 220 to 70 Hz, with a click of filtered noise and some grit.
        SoundCue.GunPlayer => Saturate(Mix(
            Mul(Osc(Saw, 0.18, t => Glide(t, 220, 70, 0.1)), Decay(0.18, 0.035)),
            Gain(Mul(Lp(Noise(seed, 0.18), 3500), Decay(0.18, 0.008)), 0.6)), 9),

        // Higher, thinner square burst at 600 Hz falling to 300, with a band of hissy noise: a small rattle.
        SoundCue.GunEnemy => Hp(Mix(
            Mul(Osc(Square, 0.15, t => Glide(t, 600, 300, 0.08)), Decay(0.15, 0.02)),
            Gain(Mul(Bp(Noise(seed, 0.15), 2500, 1), Decay(0.15, 0.015)), 0.7)), 350),

        // Low wet blorp: a sine that bubbles up from 100 Hz to about 260 Hz and back, with a soft noise gurgle.
        SoundCue.OilDrop => Mix(
            Mul(Osc(Sine, 0.45, t => 100 + 160 * Math.Sin(Math.PI * Math.Min(t / 0.3, 1))), Adsr(0.45, 0.02, 0.1, 0.6, 0.12, 0.3)),
            Gain(Mul(Lp(Noise(seed, 0.45), 500), Decay(0.45, 0.12)), 0.4)),

        // Hiss burst: noise with a falling low-pass sweep from 9 kHz to 2.5 kHz, over a faint low thump.
        SoundCue.SmokeDrop => Mix(
            Mul(Sweep(Hp(Noise(seed, 0.6), 1500), FilterKind.LowPass, 9000, 2500, 0.5),
                Adsr(0.6, 0.03, 0.15, 0.5, 0.25, 0.3)),
            Gain(Mul(Osc(Sine, 0.6, 70), Decay(0.6, 0.05)), 0.4)),

        // Metallic scatter: a dozen short inharmonic pings (700-1300 Hz) at random times, each quieter than the last.
        SoundCue.CaltropsDrop => Hall(Caltrops(seed), 0.5, 0.25, 0.2),

        // Rising whoosh: noise whose band sweeps up from 400 Hz to 5 kHz, with a saw climbing from 80 to 220 Hz.
        SoundCue.MissileLaunch => Saturate(Mix(
            Mul(Sweep(Noise(seed, 0.75), FilterKind.BandPass, 400, 5000, 0.5, 1.2), Adsr(0.75, 0.4, 0.1, 0.9, 0.2, 0.5)),
            Gain(Mul(Osc(Saw, 0.75, t => Glide(t, 80, 220, 0.5)), Adsr(0.75, 0.3, 0.1, 0.8, 0.25, 0.5)), 0.5)), 6),

        // A radio blip (two short 1 kHz squares through a narrow band), then a falling zap from 1.3 kHz to 120 Hz.
        SoundCue.StrikeCall => Mix(
            Gain(Bp(Mix(Mul(Osc(Square, 0.07, 1000), Decay(0.07, 0.05)),
                        Place(Mul(Osc(Square, 0.07, 1000), Decay(0.07, 0.05)), 0.1)), 1100, 2), 1.5),
            Place(Mul(Osc(Saw, 0.5, t => Glide(t, 1300, 120, 0.4)), Adsr(0.5, 0.005, 0.1, 0.7, 0.15, 0.3)), 0.25)),

        // Very deep boom: a 55 Hz sine sinking to 28 Hz and dark noise, in a big FDN hall.
        SoundCue.StrikeBoom => Hall(Boom(seed, 3.2, 1500, 120, 1.0, 55, 28, 1.2, 1.2), 3.6, 2.2, 0.35),

        // ---- damage ----
        // Explosion: bright noise closing to a dark rumble, plus a 80 to 35 Hz thump, in a gated reverb.
        SoundCue.Explosion => GatedReverb(Boom(seed, 1.3, 7000, 250, 0.35, 80, 35, 0.25, 1.0), 1.4, 1.0),

        // Metallic crunch: ringing 310 and 477 Hz squares, a noise snap around 1.8 kHz and a dull thud, driven hard.
        SoundCue.PlayerHit => GatedReverb(Hp(Saturate(Mix(
            Mul(Mix(Osc(Square, 0.3, 310), Osc(Square, 0.3, 477)), Decay(0.3, 0.06)),
            Gain(Mul(Bp(Noise(seed, 0.3), 1800, 0.8), Decay(0.3, 0.04)), 1.2),
            Gain(Mul(Osc(Sine, 0.3, 100), Decay(0.3, 0.05)), 0.8)), 10), 150), 0.3, 0.22, 0.3, 0.25),

        // Big wreck: a long explosion with crackles, over a dying engine (a saw falling from 90 to 20 Hz).
        SoundCue.PlayerWrecked => Hall(Mix(
            Boom(seed, 3.5, 8000, 200, 0.7, 60, 25, 0.8, 1.3),
            Crackle(seed + 1, 3.0, 0.9),
            Gain(Mul(Osc(Saw, 1.4, t => Glide(t, 90, 20, 1.2)), Decay(1.4, 0.6)), 0.6)), 3.8, 2.0, 0.3),

        // Busted: a siren stab flipping between 760 and 600 Hz, then a falling A minor chord under chorus.
        SoundCue.PlayerBusted => Hall(Chorus(Mix(
            Mul(Osc(Square, 0.8, t => (int)(t / 0.16) % 2 == 0 ? 760 : 600), Adsr(0.8, 0.01, 0.05, 0.6, 0.15, 0.6)),
            Gain(Place(FallingChord(), 0.5), 1.0)), 0.5), 3.4, 1.5, 0.3),

        // Pop then hiss: a sine popping from 300 to 80 Hz with a click, then 3 kHz+ noise leaking away.
        SoundCue.Puncture => Mix(
            Mul(Osc(Sine, 0.5, t => Glide(t, 300, 80, 0.05)), Decay(0.5, 0.02)),
            Gain(Mul(Noise(seed, 0.5), Decay(0.5, 0.004)), 0.5),
            Gain(Mul(Hp(Noise(seed + 1, 0.5), 3000), Adsr(0.5, 0.01, 0.2, 0.3, 0.2, 0.15)), 0.5)),

        // Tyre squeal: a wobbling square (700 to 1000 Hz and back) with a hissy band of noise, swelling then dropping.
        SoundCue.Spin => Mul(Bp(Mix(
            Osc(Square, 0.7, t => 700 + 300 * Math.Sin(Math.PI * t / 0.7) + 25 * Math.Sin(2 * Math.PI * 28 * t)),
            Gain(Noise(seed, 0.7), 0.4)), 1300, 0.7), Adsr(0.7, 0.06, 0.1, 0.8, 0.15, 0.5)),

        // ---- pickups and warnings ----
        // Bright-ish rising arpeggio of square notes C5 E5 G5 C6 under chorus (all below 1.1 kHz).
        SoundCue.Pickup => Hall(Chorus(Mix(Arp(Square, [523.25, 659.25, 783.99, 1046.5], 0.07, 0.14)), 0.5), 0.5, 0.2, 0.2),

        // Deeper chord swell: a low C3 saw, then a C minor-ish stack (C3 G3 C4 Eb4), chorused, with a soft thump.
        SoundCue.PickupArmor => Hall(Chorus(Mix(
            Lp(Mul(Mix(Osc(Saw, 0.7, 130.8), Osc(Saw, 0.7, 196), Osc(Saw, 0.7, 261.6), Osc(Saw, 0.7, 311.1)),
                Adsr(0.7, 0.06, 0.1, 0.7, 0.25, 0.4)), 1400),
            Gain(Mul(Osc(Sine, 0.7, 65.4), Decay(0.7, 0.2)), 1.5)), 0.6, 4, 0.6), 0.75, 0.3, 0.2),

        // Urgent: two bright square notes, 2.4 kHz then 1.8 kHz, pushed into saturation. Bright: warning only.
        SoundCue.Warning => Saturate(Mix(
            Mul(Osc(Square, 0.16, 2400), Adsr(0.16, 0.003, 0.02, 0.9, 0.02, 0.13)),
            Place(Mul(Osc(Square, 0.16, 1800), Adsr(0.16, 0.003, 0.02, 0.9, 0.02, 0.13)), 0.2)), 6),

        // Urgent alarm: a piercing square flipping between 2.6 and 1.9 kHz twelve times a second. Bright: alarm only.
        SoundCue.MissileAlarm => Saturate(Mul(
            Osc(Square, 0.75, t => (int)(t * 12) % 2 == 0 ? 2600 : 1900),
            Adsr(0.75, 0.005, 0.02, 0.95, 0.03, 0.7)), 6),

        // ---- boss ----
        // Deep brass stab: detuned saws at 55 and 82 Hz, a filter opening 250 to 2000 Hz, chorus, echo and a hall.
        SoundCue.BossAppears => Hall(Echo(Chorus(BrassStab(3.0, 55, 82.4, 0.9, 0.5, 2000), 0.5), 3.0, 330, 0.4, 0.3), 3.0, 1.2, 0.2),

        // Shorter stab, an octave-and-a-bit higher (73 and 110 Hz), snappier, with a short echo.
        SoundCue.BossPhase => Echo(Chorus(BrassStab(0.75, 73.4, 110, 0.3, 0.12, 1800), 0.5), 0.78, 130, 0.35, 0.25),

        // Metallic clank: a bell-like sum of sines (340 Hz times 1, 2.76, 5.4, 8.9) with a noise tick and a ring.
        SoundCue.BossHit => GatedReverb(Mix(
            Mul(Mix(Osc(Sine, 0.4, 340), Gain(Osc(Sine, 0.4, 340 * 2.76), 0.7),
                    Gain(Osc(Sine, 0.4, 340 * 5.4), 0.4), Gain(Osc(Sine, 0.4, 340 * 8.9), 0.2)), Decay(0.4, 0.1)),
            Gain(Mul(Hp(Noise(seed, 0.4), 1500), Decay(0.4, 0.01)), 0.8)), 0.4, 0.3, 0.35, 0.25),

        // ---- enemy missiles ----
        // Ignition: a low saw whump (90 Hz) with a crackle of hot noise above 2 kHz, quick and dry.
        SoundCue.EnemyMissileLaunch => Mix(
            Mul(Lp(Osc(Saw, 0.5, t => Glide(t, 110, 60, 0.2)), 500), Adsr(0.5, 0.02, 0.15, 0.4, 0.2, 0.25)),
            Gain(Mul(Hp(Noise(seed, 0.5), 2000), Adsr(0.5, 0.02, 0.2, 0.3, 0.2, 0.2)), 0.5)),

        // Whoosh past: noise whose band rises to 2.5 kHz and falls back, like something passing by; a faint falling saw.
        SoundCue.EnemyMissileDodged => Mix(
            Mul(PassBy(seed, 0.6), Adsr(0.6, 0.25, 0.05, 0.9, 0.3, 0.3)),
            Gain(Mul(Lp(Osc(Saw, 0.6, t => Glide(t, 600, 200, 0.5)), 900), Adsr(0.6, 0.25, 0.05, 0.9, 0.3, 0.3)), 0.3)),

        // Small pop: a quick sine pluck from 500 to 120 Hz and a short burst of 2 kHz noise sparks.
        SoundCue.EnemyMissileShotDown => Mix(
            Mul(Osc(Sine, 0.3, t => Glide(t, 500, 120, 0.04)), Decay(0.3, 0.035)),
            Gain(Mul(Bp(Noise(seed, 0.3), 2000, 1), Decay(0.3, 0.02)), 0.7),
            Place(Gain(Mul(Bp(Noise(seed + 1, 0.2), 1200, 1), Decay(0.2, 0.015)), 0.5), 0.03)),

        // ---- police and road ----
        // Siren whoop: a saw rising from 400 to 900 Hz and holding, soft-filtered, chorused.
        SoundCue.HeatUp => Chorus(Lp(Mul(Osc(Saw, 0.75, t => Glide(t, 400, 900, 0.4)),
            Adsr(0.75, 0.02, 0.1, 0.8, 0.15, 0.6)), 2200), 0.5),

        // A falling triangle tone from 700 down to 180 Hz, gentle, with a little room.
        SoundCue.HeatDown => Hall(Mul(Osc(Triangle, 0.65, t => Glide(t, 700, 180, 0.55)),
            Adsr(0.65, 0.01, 0.1, 0.8, 0.2, 0.45)), 0.7, 0.2, 0.15),

        // Camera-flash zap: a square falling from 1.2 kHz to 150 Hz in a tenth of a second, with a pop of noise.
        SoundCue.SpeedTrap => Mix(
            Mul(Osc(Square, 0.4, t => Glide(t, 1200, 150, 0.1)), Decay(0.4, 0.07)),
            Gain(Mul(Hp(Noise(seed, 0.4), 800), Decay(0.4, 0.012)), 0.8)),

        // Crash: five metal impacts in half a second (noise bands and low rings) on a thump, in a gated reverb.
        SoundCue.RoadblockCrash => GatedReverb(Crash(seed, 1.4), 1.45, 1.1, 0.6, 0.35),

        // ---- menus ----
        // Soft tick: a 650 Hz triangle, 40 ms.
        SoundCue.MenuMove => Mul(Osc(Triangle, 0.06, 650), Decay(0.06, 0.012)),

        // Blip: 600 then 900 Hz triangle steps.
        SoundCue.MenuConfirm => Mix(
            Mul(Osc(Triangle, 0.07, 600), Adsr(0.07, 0.003, 0.02, 0.8, 0.02, 0.055)),
            Place(Mul(Osc(Triangle, 0.12, 900), Adsr(0.12, 0.003, 0.03, 0.7, 0.05, 0.07)), 0.07)),

        // Lower blip: 500 then 330 Hz triangle steps (going back).
        SoundCue.MenuBack => Mix(
            Mul(Osc(Triangle, 0.07, 500), Adsr(0.07, 0.003, 0.02, 0.8, 0.02, 0.055)),
            Place(Mul(Osc(Triangle, 0.12, 330), Adsr(0.12, 0.003, 0.03, 0.7, 0.05, 0.07)), 0.07)),

        // Tick for a typed letter: an 880 Hz sine with a tiny noise click.
        SoundCue.MenuLetter => Mix(
            Mul(Osc(Sine, 0.05, 880), Decay(0.05, 0.01)),
            Gain(Mul(Hp(Noise(seed, 0.05), 3000), Decay(0.05, 0.002)), 0.3)),

        _ => throw new ArgumentOutOfRangeException(nameof(cue)),
    };

    // ---- shared pieces ----

    /// <summary>A noise burst closing from bright to dark, plus a falling sine thump, driven for grit: the core of every explosion.</summary>
    private static float[] Boom(int seed, double seconds, double hiHz, double loHz, double noiseTau,
        double thumpFrom, double thumpTo, double thumpTau, double thumpGain)
    {
        var noise = Mul(Sweep(Noise(seed, seconds), FilterKind.LowPass, hiHz, loHz, noiseTau * 2), Decay(seconds, noiseTau));
        var thump = Mul(Osc(Sine, seconds, t => Glide(t, thumpFrom, thumpTo, 0.4)), Decay(seconds, thumpTau));
        return Saturate(Mix(noise, Gain(thump, thumpGain * 1.5)), 8);
    }

    /// <summary>Sparse random crackles of noise, for burning wreckage.</summary>
    private static float[] Crackle(int seed, double seconds, double density)
    {
        var rng = new Random(seed);
        var parts = new List<float[]>();
        for (int i = 0; i < 25; i++)
        {
            double at = rng.NextDouble() * seconds * 0.85 * density;
            var bit = Mul(Hp(Noise(seed + 10 + i, 0.05), 1000), Decay(0.05, 0.008));
            parts.Add(Gain(Place(bit, at), 0.35 * Math.Exp(-at / 1.2)));
        }
        return Fit(Mix(parts.ToArray()), seconds);
    }

    /// <summary>Metallic scatter: pings at random times and pitches.</summary>
    private static float[] Caltrops(int seed)
    {
        var rng = new Random(seed);
        var parts = new List<float[]>();
        for (int i = 0; i < 12; i++)
        {
            double at = rng.NextDouble() * 0.3;
            double hz = 700 + rng.NextDouble() * 600;
            var ping = Mul(Mix(Osc(Square, 0.1, hz), Gain(Osc(Square, 0.1, hz * 1.51), 0.5)), Decay(0.1, 0.012));
            parts.Add(Gain(Place(ping, at), 1.0 - i * 0.05));
        }
        return Fit(Hp(Mix(parts.ToArray()), 500), 0.5);
    }

    /// <summary>Notes in turn, each <paramref name="step"/> seconds after the last and <paramref name="length"/> long.</summary>
    private static float[][] Arp(OscillatorWave wave, double[] hz, double length, double step)
    {
        var parts = new float[hz.Length][];
        for (int i = 0; i < hz.Length; i++)
            parts[i] = Place(Mul(Osc(wave, length * 2.5, hz[i]), Decay(length * 2.5, length * 0.8)), i * step);
        return parts;
    }

    /// <summary>A minor chord (A2 C3 E3) in saws that all slide down by a third of their pitch, fading slowly.</summary>
    private static float[] FallingChord()
    {
        const double length = 2.6;
        float[] Voice(double hz) => Osc(Saw, length, t => hz * (1 - 0.35 * Math.Min(t / 2.0, 1)));
        return Lp(Mul(Mix(Voice(110), Voice(130.8), Voice(164.8)), Adsr(length, 0.05, 0.3, 0.6, 1.2, 1.2)), 1100);
    }

    /// <summary>Two detuned saws a fifth apart through a low-pass that opens then closes: a synth-brass stab.</summary>
    private static float[] BrassStab(double seconds, double lowHz, double highHz, double hold, double open, double peakHz)
    {
        var raw = Mix(Osc(Saw, seconds, lowHz), Osc(Saw, seconds, lowHz * 1.006), Osc(Saw, seconds, highHz), Osc(Saw, seconds, highHz * 0.994));
        var enveloped = Mul(raw, Adsr(seconds, 0.02, 0.2, 0.7, seconds * 0.4, hold));
        var up = Sweep(enveloped, FilterKind.LowPass, 250, peakHz, open);
        return Saturate(up, 5);
    }

    /// <summary>A band of noise that rises to 2.5 kHz and falls again: something going past.</summary>
    private static float[] PassBy(int seed, double seconds)
    {
        var noise = Noise(seed, seconds);
        var up = Sweep(noise, FilterKind.BandPass, 400, 2500, seconds / 2, 1.2);
        var down = Sweep(noise, FilterKind.BandPass, 2500, 400, seconds / 2, 1.2);
        var result = new float[noise.Length];
        int half = result.Length / 2;
        for (int i = 0; i < result.Length; i++) result[i] = i < half ? up[i] : down[i];
        return result;
    }

    /// <summary>Five metal impacts and a thump: a car going through a roadblock.</summary>
    private static float[] Crash(int seed, double seconds)
    {
        var rng = new Random(seed);
        double[] times = [0, 0.06, 0.15, 0.31, 0.52];
        var parts = new List<float[]> { Gain(Mul(Osc(Sine, seconds, t => Glide(t, 90, 40, 0.3)), Decay(seconds, 0.3)), 1.5) };
        for (int i = 0; i < times.Length; i++)
        {
            double center = 800 + rng.NextDouble() * 2200;
            double ring = 110 + rng.NextDouble() * 140;
            var hit = Mix(Mul(Bp(Noise(seed + 1 + i, 0.3), center, 0.8), Decay(0.3, 0.07)),
                          Gain(Mul(Osc(Square, 0.3, ring), Decay(0.3, 0.1)), 0.7));
            parts.Add(Gain(Place(hit, times[i]), 1.0 - i * 0.12));
        }
        return Saturate(Fit(Mix(parts.ToArray()), seconds), 8);
    }
}
