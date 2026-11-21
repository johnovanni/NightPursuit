# Night Pursuit Plan 5: Sound and Music. Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 80s night-drive synth sound effects (one-off sounds as generated WAV files, steady sounds made
live) and the owner's music in named slots, with sound settings, a mute key and failure-safe audio.

**Architecture:** Build on **NAudio's own building blocks** rather than writing our own: its mixer, panning,
fades, tone and filter blocks, envelope, limiter, reverb/chorus/delay effects, and WAV files. Only the few pieces
NAudio doesn't provide are ours.

Core gets a new `Audio/` folder that references **NAudio.Core** only. NAudio.Core is plain managed .NET with no
Windows or WPF parts, so the tests drive everything in it. The folder holds:
- the mixer graph: NAudio's `MixingSampleProvider` plus our voice limit, music cross-fade and volume buses, with
  NAudio's `LimiterEffect` at the end;
- the live voices;
- the music slot rules;
- the `SoundDirector`, which turns the screen flow, events and world state into cues.

A console tool, `tools/SynthSounds`, builds the one-off WAVs into `content/sounds/` from NAudio blocks and
effects, and those WAVs are committed.

The App adds only the Windows parts: NAudio's output device (`WaveOutEvent`), MP3/WAV decoding
(`AudioFileReader`), and a `SoundSystem` that `GameLoop` calls once per frame.

Diagnostics gain `--sound-check` (an offline mix to WAV, plus a cue log), and `--perf` gains a sound-work line.

**Tech Stack:** C# / .NET 10, WPF (`net10.0-windows`), xUnit 2.9.3, System.Text.Json, **NAudio 3.3.0**
(MIT licence). It is split as:
- `NAudio.Core` in Core, the tool and the tests;
- the full `NAudio` package in the App only. It brings `NAudio.WinMM` (with `WaveOutEvent`), `NAudio.Wasapi`,
  `NAudio.Midi` (which WinMM depends on anyway) and `AudioFileReader`, which opens WAV or MP3 as float samples.

**NAudio 3.3.0 is newer than R. Claude's training.** The class names below were checked against the package on
this PC (2026-11-21): `MixingSampleProvider`, `PanningSampleProvider`, `VolumeSampleProvider`,
`FadeInOutSampleProvider`, `MonoToStereoSampleProvider`, `WdlResamplingSampleProvider`, `SignalGenerator`,
`EnvelopeGenerator`, `BiQuadFilter`, `CrossfadingBiQuadFilter`, `WaveFileReader`/`WaveFileWriter`, `LimiterEffect`
(`CeilingDb`, `ReleaseMs`, `LookaheadMs`, `TruePeak`), `EffectSampleProvider`, `EffectChain`, `ReverbEffect`,
`FdnReverbEffect`, `ChorusEffect`, `DelayEffect`, `SaturationEffect` and `GainEffect`.

In 3.x, `ISampleProvider.Read` takes a `Span<float>`. **Implementers take exact signatures from the package's own
docs** (`NAudio.Core.xml` next to the DLL in `%USERPROFILE%\.nuget\packages\naudio.core\3.3.0\lib\net9.0\`), never
from memory of NAudio 2.x.

**Spec:** `docs/superpowers/specs/2026-11-21-sound-and-music-design.md` (the sound design, binding).
Also read the main design `docs/superpowers/specs/2026-11-17-night-pursuit-design.md`:
- §5.9 NFR-3 (nothing carried by sound alone);
- §7.6 (events);
- §8.4 (diagnostics).

In `docs/decisions.md`, read D12 (losing focus pauses), D68 (Tab cycles rear weapons) and D144 (stress length).

**Branch:** `plan-5-sound`, from `main` (b1bc483). Decisions are numbered from **D151**, in a new
`docs/decisions.md` section, "Plan 5 calls (sound and music)".

## Global Constraints

- **Frameworks:** Core and Tests target `net10.0`, and the App targets `net10.0-windows`.
  - `tools/SynthSounds` is a `net10.0` console app, added to `NightPursuit.slnx` under a `/tools/` folder.
  - `Directory.Build.props` is unchanged.
- **NAudio 3.3.0 is the only new dependency** (it brings `System.Numerics.Tensors` with it).
  - Core, the tool and the tests reference `NAudio.Core` only.
  - Only the App references the full `NAudio` package.
  - Core never references WPF, WinMM, Wasapi or the full package.
- **Use NAudio's block when it does the job.** Write our own code only where this plan says so: the voice limit,
  the music cross-fade and pause level, the click-free value ramp, the seeded noise, the final hard clamp, and the
  gliding oscillator if Task 1's check shows `SignalGenerator` jumps when its frequency changes. Anything else we'd
  write ourselves needs a logged decision saying why NAudio's version didn't fit.
- **Audio never changes the game:**
  - `World.StateHash()` and all game behaviour are unchanged;
  - all 969 existing tests pass unmodified, except the settings, menu and input tests that Task 5 extends;
  - audio code never reads or advances any `World` RNG (`Rng`, `TrafficRng`); it uses its own `System.Random`
    with a fixed seed.
- **NFR-3 (binding):** every cue repeats something already on screen, as in the design's §5.2 table. No new
  information comes by sound alone, and the game plays fully with sound off.
- **Sample format:**
  - the mixer runs at 44,100 Hz, stereo, 32-bit float, interleaved;
  - effect WAVs are 44,100 Hz, 16-bit PCM, mono;
  - the master limiter is NAudio's `LimiterEffect` with `CeilingDb = -1` (0.891 linear), `LookaheadMs` of 2 or
    less (it adds delay, so keep it short) and `ReleaseMs = 100`;
  - a hard clamp at 0.891 follows the limiter as a safety net, so no output sample ever exceeds it;
  - every stage of the mix is an NAudio `ISampleProvider`.
- **Volumes:**
  - settings run 0–10;
  - gain = 0 at 0, otherwise 10^((v − 10) × 3 / 20), which is 3 dB per step with 10 = 1.0;
  - defaults are sound ON, music 7, effects 8.
- **Diagnostics never open a sound device:** `--snapshot`, `--stress`, `--perf` and `--sound-check` all stay
  silent.
- **Perf:** the render mean stays under 8 ms. The per-frame sound work on the game thread averages under 0.5 ms.
- **On-screen text** uses the existing upper-case style, for example `SOUND OFF` and `MUSIC FILE NOT FOUND: X.MP3`.
- **Visual checks** only with the app's own `--snapshot`, never screen capture. Keep exe launches to about one per
  task, and never re-run a launch that the permission system refused.
- **The owner's music files are committed** (owner ruling). The game ships with every music slot empty until they
  arrive.
- **Every judgement call** goes in `docs/decisions.md` (D151 onward), in plain words.

## Review Focus

1. **Paused or covered frames:** `World.Events` keeps the last advance's events while the game is not stepped. Sounds
   must fire once, not again on every paused frame. Pinned in Task 7
   (`Paused_frames_do_not_replay_the_last_events`).
2. **A new world** (a practice restart, the next mission, a respawn): steady sounds from the old world must stop.
   The mission theme restarts on a new world but keeps going through a respawn. Pinned in Task 6 and Task 7.
3. **Broken audio:** no sound device, a missing or bad music file, or the device going away. The game keeps running
   and shows one notice, with no crash and no freeze. Pinned in Task 6 (manifest) and Task 8 (output).
4. **A long stalled frame full of events:** the voice cap holds, and the mix never passes the ceiling. Pinned in
   Task 3 (`A_burst_of_fifty_explosions_never_passes_the_ceiling`).
5. **Settings from an older or hand-edited file:** missing sound fields, or values like `42` or `"loud"`. They load
   as defaults or are clamped. Pinned in Task 5.

---

### Task 1: NAudio.Core, its checks, and our few small pieces

**Files:**
- Modify: `src/NightPursuit.Core/NightPursuit.Core.csproj` (`<PackageReference Include="NAudio.Core" Version="3.3.0" />`)
- Create: `src/NightPursuit.Core/Audio/AudioFormat.cs`, `Smoothed.cs`, `SeededNoise.cs`, `HardClamp.cs`,
  `SoundFiles.cs`, and `GlideOscillator.cs` (only if Step 3's check needs it)
- Test: `tests/NightPursuit.Core.Tests/Audio/NAudioChecksTests.cs`, `SmallPiecesTests.cs`

**Interfaces (produces):**
- `static class AudioFormat`: `const int SampleRate = 44100`, `WaveFormat Stereo` (IEEE float, 44,100 Hz, 2 channels)
  and `WaveFormat Mono` (IEEE float, 44,100 Hz, 1 channel).
- `sealed class Smoothed(double seconds)`: `double Target`, `double Next()`. A linear ramp to the target over the
  time given, for click-free parameter changes. It exists because NAudio's `VolumeSampleProvider` jumps. If the
  package docs show `GainEffect` ramps its gain, use that for the volume buses and keep `Smoothed` only for synth
  parameters.
- `sealed class SeededNoise(int seed) : ISampleProvider` (mono): white noise in [−1, 1) from `System.Random(seed)`.
  The tool's sounds need to repeat exactly, and `SignalGenerator`'s noise has no seed.
- `sealed class HardClamp(ISampleProvider source, float ceiling = 0.891f) : ISampleProvider`: the safety net after
  `LimiterEffect`.
- `static class SoundFiles`:
  - `float[] LoadMono(string path)`: uses `WaveFileReader`, accepts 16-bit PCM at 44,100 Hz, mono or stereo (stereo
    is averaged to mono), and throws `InvalidDataException` with a plain message for anything else;
  - `void WriteMono16(string path, ReadOnlySpan<float> samples)` and
    `void WriteStereo16(string path, ReadOnlySpan<float> interleaved)`: both use `WaveFileWriter`.
- `GlideOscillator : ISampleProvider` (mono): `Wave` (Sine/Saw/Square/Triangle) and a settable `Frequency`, with a
  phase accumulator so pitch changes never jump. **Create it only if** the `SignalGenerator` check below fails.
  Otherwise the live voices use `SignalGenerator`, and the decision is logged.

- [ ] **Step 1: Write the NAudio checks.** These pin down how NAudio 3.3.0 behaves, so later tasks rely on facts:
  - `SignalGenerator_sine_at_441_Hz_crosses_zero_882_times_a_second` (±2);
  - `SignalGenerator_frequency_change_is_or_is_not_phase_continuous`: record the largest sample-to-sample step
    after a 100 → 200 Hz change mid-buffer. The test asserts whichever way it really behaves, and its name and
    comment say which; that result decides `GlideOscillator`;
  - `LimiterEffect_at_minus_1_dB_with_hard_clamp_never_passes_0_891`: random stereo noise ×4 through
    `EffectSampleProvider`, `LimiterEffect` and `HardClamp`;
  - `LimiterEffect_leaves_quiet_audio_unchanged`: a 0.3-peak sine comes out the same to within 1e-3, after
    allowing for `LatencySamples`;
  - `LimiterEffect_latency_is_under_3_ms`;
  - `BiQuadFilter_low_pass_at_200_Hz_cuts_a_5_kHz_sine_by_at_least_20_dB`;
  - `EnvelopeGenerator_rises_holds_and_releases`;
  - `MixingSampleProvider_with_ReadFully_fills_the_buffer_when_empty`;
  - `PanningSampleProvider_full_left_is_silent_on_the_right`.
- [ ] **Step 2: Write tests for our pieces:**
  - `Smoothed_reaches_its_target_in_time_without_overshoot`;
  - `SeededNoise_with_the_same_seed_repeats`;
  - `HardClamp_never_passes_its_ceiling`;
  - `Wav_round_trip_keeps_samples_to_16_bit_precision`;
  - `Loading_an_8_bit_or_48_kHz_wav_throws_a_plain_message`;
  - and, only if `GlideOscillator` is needed, `GlideOscillator_frequency_change_keeps_the_phase_continuous`.
- [ ] **Step 3: Add the package and implement our pieces.** Use the package's `NAudio.Core.xml` for exact
  signatures. Log the `SignalGenerator` result and the `GainEffect` finding as decisions.
- [ ] **Step 4: Run `dotnet test`.** All tests pass (969 + the new ones).
- [ ] **Step 5: Commit** `feat(audio): NAudio.Core with behaviour checks, and our small audio pieces`

### Task 2: Sound cues and the generator tool

**Files:**
- Create: `src/NightPursuit.Core/Audio/SoundCue.cs`
- Create: `tools/SynthSounds/SynthSounds.csproj` (references Core), `tools/SynthSounds/Program.cs`,
  `tools/SynthSounds/Recipes.cs`
- Modify: `NightPursuit.slnx` (add a `/tools/` folder with the project)
- Create (generated, committed): `content/sounds/<cue>.wav` for every cue, and `content/sounds/report.txt`
- Test: `tests/NightPursuit.Core.Tests/Audio/SoundFilesTests.cs`

**Interfaces (produces):**
- `public enum SoundCue`, each mapped to one file `content/sounds/<kebab-name>.wav`:
  - weapons: `GunPlayer, GunEnemy, OilDrop, SmokeDrop, CaltropsDrop, MissileLaunch, StrikeCall, StrikeBoom`;
  - damage: `Explosion, PlayerHit, PlayerWrecked, PlayerBusted, Puncture, Spin`;
  - pickups and warnings: `Pickup, PickupArmor, Warning, MissileAlarm`;
  - boss: `BossAppears, BossPhase, BossHit`;
  - enemy missiles: `EnemyMissileLaunch, EnemyMissileDodged, EnemyMissileShotDown`;
  - police and road: `HeatUp, HeatDown, SpeedTrap, RoadblockCrash`;
  - menus: `MenuMove, MenuConfirm, MenuBack, MenuLetter`.
- `static class SoundCues`:
  - `string FileName(SoundCue c)`: kebab case plus `.wav`, for example `GunPlayer` → `gun-player.wav`;
  - `double MaxSeconds(SoundCue c)`: 4.0 for `PlayerWrecked`, `PlayerBusted`, `StrikeBoom` and `BossAppears`;
    1.5 for `Explosion` and `RoadblockCrash`; 0.8 for everything else.
- **The tool:**
  - `dotnet run --project tools/SynthSounds -- <content/sounds dir>` writes every cue's WAV;
  - it also writes `report.txt`: one line per cue with the length in seconds, the peak in dBFS and the RMS in dBFS;
  - it exits with 1 if any peak is above −1 dBFS or any length is over `MaxSeconds`;
  - it is deterministic: every noise source uses a fixed seed per cue;
  - it never overwrites a file named in `content/sounds/keep.txt` (one file name per line). That way an effect
    the owner has swapped for their own survives a re-run. The repo ships the file empty, with a comment line
    explaining it.
- **Recipes** follow the design's §5.1–5.2 sound column (80s synth: saw, square, pulse, filtered noise, pitch sweeps,
  punchy envelopes, bass-led).
  - They are built from NAudio blocks: `SignalGenerator` (including its sweep mode for pitch sweeps),
    `SeededNoise`, `BiQuadFilter` and `EnvelopeGenerator`.
  - NAudio's effects give the 80s character: a short gated `ReverbEffect`/`FdnReverbEffect` on hits and explosions,
    `ChorusEffect` on the pads and stabs, `DelayEffect` on the boss stab, and `SaturationEffect` for grit.
  - The exact recipes are the builder's call; log each one's character in one line in the decisions log.
- The tool references Core, and through it `NAudio.Core`. It writes with `SoundFiles.WriteMono16`.
  - Peaks are normalised to −3 dBFS.
  - The two urgent cues, `MissileAlarm` and `Warning`, are the only bright, high tones (over 1.5 kHz fundamental).

- [ ] **Step 1: Write failing tests**
  - `Every_cue_has_its_wav_file`;
  - `Every_sound_file_is_44_1_kHz_16_bit_mono_and_reads`;
  - `No_sound_peaks_above_minus_1_dBFS`;
  - `No_sound_is_longer_than_its_limit`;
  - `Every_sound_starts_and_ends_near_silence`: the first and last 2 ms are under −40 dBFS, so there are no clicks;
  - `File_names_are_kebab_case`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement the enum, the tool and the recipes. Run the tool to generate the files.**
- [ ] **Step 4: Run `dotnet test` and run the tool again.** All tests pass. The tool exits 0, and re-running it
  leaves every WAV byte-identical (`git status` is clean after the second run).
- [ ] **Step 5: Commit** `feat(audio): synth sound generator and the effect sounds`

### Task 3: The mixer (built on NAudio's mixer)

**Files:**
- Create: `src/NightPursuit.Core/Audio/AudioMixer.cs`, `SoundBank.cs`, `VoiceLimit.cs`, `MusicChannel.cs`
- Test: `tests/NightPursuit.Core.Tests/Audio/AudioMixerTests.cs`

**Interfaces:**
- Consumes: Task 1 (`AudioFormat`, `Smoothed`, `HardClamp`, `SoundFiles`) and Task 2 (`SoundCue`,
  `SoundCues.FileName`).
- Produces:
  - `sealed class SoundBank`:
    - `static SoundBank Load(string soundsDir, out IReadOnlyList<string> problems)`: a missing or bad file adds a
      problem and leaves that cue silent;
    - `float[]? Samples(SoundCue c)`: loaded once into memory.
  - `readonly record struct CuePlay(SoundCue Cue, double Volume = 1, double Pan = 0)`: volume 0–1, pan −1 (left) to
    +1 (right).
  - `sealed class AudioMixer(SoundBank bank) : ISampleProvider` (stereo float, 44,100 Hz):
    - `void Play(CuePlay p)`: thread-safe;
    - `int Read(Span<float>)`: always fills the whole buffer;
    - `bool SoundOn`, `int MusicVolume`, `int EffectsVolume`: the gain rule from Global Constraints, smoothed over
      20 ms;
    - `LiveVoices Live`: Task 4 plugs in here; until then a stub that outputs silence;
    - `void SetMusic(ISampleProvider? next, double fadeSeconds)`: cross-fades out the current track and in the next;
      null fades to silence;
    - `double MusicLevel`: 1.0, or 0.4 while paused, ramped over 0.3 s.
- **How it's put together** (NAudio blocks unless marked *ours*):
  - **Each one-off sound:** the cue's samples (a small mono `ISampleProvider` over the cached array), then
    `VolumeSampleProvider` (its fixed volume), then `PanningSampleProvider`.
  - **Effects bus:** `MixingSampleProvider`, with each finished voice removed through `MixerInputEnded`. In front of
    it sits *ours*, `VoiceLimit`: it keeps at most **24** one-off voices, and a 25th drops the quietest (the lowest
    volume × gain still to play).
  - **Live voices** (Task 4) join the effects bus.
  - **Music bus:** *ours*, `MusicChannel`. It holds the current and the next track, each in a
    `FadeInOutSampleProvider` (one fading out, one fading in), mixed by a `MixingSampleProvider`. The old track is
    dropped once its fade ends.
  - **Volume buses** on the effects and music buses: `GainEffect` if it ramps (Task 1's finding), otherwise
    `Smoothed`.
  - **Master:** `MixingSampleProvider` (`ReadFully = true`), then `EffectSampleProvider` with `LimiterEffect`, then
    `HardClamp`.
  - **Sound off:** `SoundOn = false` outputs zeros, but voices and music keep running underneath, so nothing jumps
    when sound comes back.

- [ ] **Step 1: Write failing tests**
  - `A_cue_plays_its_samples_then_ends`;
  - `Pan_left_puts_the_sound_in_the_left_channel`;
  - `Effects_volume_0_is_silent_and_10_is_full`;
  - `Volume_steps_are_3_dB`;
  - `A_volume_change_ramps_without_a_click`;
  - `Sound_off_writes_silence`;
  - `The_25th_voice_drops_the_quietest`;
  - `A_burst_of_fifty_explosions_never_passes_the_ceiling`;
  - `Music_cross_fade_takes_the_time_given_and_ends_on_the_new_track`;
  - `Paused_music_level_ramps_to_40_percent`;
  - `A_missing_sound_file_is_reported_and_silent`;
  - `Play_from_another_thread_while_reading_does_not_throw`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run `dotnet test`.** All tests pass.
- [ ] **Step 5: Commit** `feat(audio): mixer on NAudio's mixer, with voice limit, music cross-fade and limiter`

### Task 4: Live voices (engine, rumble, skid, siren, missile whine)

**Files:**
- Create: `src/NightPursuit.Core/Audio/LiveVoices.cs`, `LiveState.cs`
- Modify: `src/NightPursuit.Core/Audio/AudioMixer.cs` (use the real `LiveVoices`)
- Test: `tests/NightPursuit.Core.Tests/Audio/LiveVoicesTests.cs`

**Interfaces:**
- Produces:
  - `sealed record LiveState`, with these members:
    - `bool EngineOn`
    - `double Speed01` (0 to 1, a fraction of the player's top speed)
    - `double Throttle01`
    - `bool Punctured`
    - `double OffRoad01`
    - `double Skid01`
    - `double Siren01`
    - `double SirenPan`
    - `double Whine01`
    - `double WhineCloseness01`

    `static LiveState Silent` has everything off.
  - `sealed class LiveVoices : ISampleProvider` (stereo float, 44,100 Hz, joins the effects bus):
    - `void Set(LiveState s)`: called from the game thread, smoothed into the audio thread over 30 ms;
    - `int Read(Span<float>)`: always fills the buffer.
- **Built from:** `SignalGenerator` (or `GlideOscillator`, per Task 1's finding) for the tones, `SeededNoise` for
  the noise, and `BiQuadFilter` for the filters. Use `CrossfadingBiQuadFilter` where the cutoff moves every frame,
  such as the engine's throttle filter, so filter changes don't click. Levels use `Smoothed`.
- **Values pinned:**
  - **Engine:**
    - two saw oscillators detuned by ±6 cents, plus a sine one octave down;
    - the fundamental goes from 38 Hz at speed 0 to 110 Hz at top speed;
    - a low-pass cutoff from 300 Hz (no throttle) to 1,800 Hz (full throttle);
    - a peak level of −18 dBFS before the effects volume;
    - while punctured, a pitch wobble of ±4% at 6 Hz.
  - **Off-road:** low-passed noise at 500 Hz, level = `OffRoad01` × −20 dBFS.
  - **Skid:** band-limited noise around 2.5 kHz with a slight pitch wobble, at −22 dBFS × `Skid01`.
  - **Siren:**
    - two square-wave tones, 650 Hz and 900 Hz, alternating every 0.45 s, through a low-pass at 2.5 kHz;
    - level −20 dBFS × `Siren01`, panned by `SirenPan`.
  - **Whine:** a sine rising from 900 Hz to 2,200 Hz as `WhineCloseness01` goes from 0 to 1, at −24 dBFS × `Whine01`.
- **Behaviour:** every voice fades out over 30 ms when its level reaches 0, so there are no clicks.

- [ ] **Step 1: Write failing tests**
  - `Silent_state_adds_nothing`;
  - `Engine_pitch_rises_with_speed`: the zero-crossing rate at `Speed01` 1 is greater than 2.5× the rate at 0;
  - `Throttle_brightens_the_engine`: there is more energy above 600 Hz with throttle 1 than with 0;
  - `Switching_the_engine_off_fades_without_a_click`: no sample-to-sample step is over 0.05;
  - `A_jump_in_every_parameter_causes_no_click`;
  - `Siren_alternates_between_two_tones`;
  - `Siren_pan_right_is_louder_on_the_right`;
  - `Engine_peak_stays_near_minus_18_dBFS`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement. Wire the voices into `AudioMixer`.**
- [ ] **Step 4: Run `dotnet test`.** All tests pass.
- [ ] **Step 5: Commit** `feat(audio): live engine, rumble, skid, siren and missile whine`

### Task 5: Sound settings, the settings menu and the M key

**Files:**
- Modify: `src/NightPursuit.Core/Session/GameSettings.cs`, `GameStore.cs` (load and save),
  `ScreenFlow.cs` (settings rows, M toggles), `src/NightPursuit.Core/Control/GameAction.cs`
  (`ActionKind.ToggleSound`), `src/NightPursuit.App/Input/InputHub.cs` (Key.M → `ToggleSound` in play),
  `src/NightPursuit.App/Rendering/Overlays.cs` (the help line lists `M sound on/off`; also update the Controls
  screen if it lists the keys separately)
- Test: extend `tests/NightPursuit.Core.Tests` (the existing settings, store and screen-flow test files)

**Interfaces (produces):**
- `GameSettings`: `bool SoundOn = true`, `int MusicVolume = 7`, `int EffectsVolume = 8`.
  - `Clone()` copies them.
- JSON keys: `soundOn` (bool), `musicVolume` and `effectsVolume` (ints 0–10).
  - A missing key, or one of the wrong type, keeps the default.
  - An out-of-range number is clamped to 0–10.
- Settings menu: three rows after TEXT SIZE, before KEY BINDINGS:
  - `sound` "SOUND", ON/OFF; Left, Right or Confirm toggles it;
  - `music` "MUSIC VOLUME", shown as the number; Left −1, Right +1, clamped, Confirm does nothing;
  - `effects` "EFFECTS VOLUME", the same as music.
- `ScreenFlow.ToggleSound()`: flips `SoundOn` and saves the settings at once (it posts any save problem, like
  `LeaveSettings`).
  - It is called for `ActionKind.ToggleSound` in play, which is never passed on to the game.
  - It is called for `MenuKey(Letter, 'M')` on every screen except `Screen.EnterInitials`, where M types as
    before.

- [ ] **Step 1: Write failing tests**
  - `New_settings_have_sound_on_music_7_effects_8`;
  - `Sound_settings_save_and_load`;
  - `An_old_settings_file_without_sound_fields_loads_the_defaults`;
  - `Volume_42_loads_as_10_and_minus_3_as_0`;
  - `A_volume_of_the_wrong_type_keeps_the_default` (for example `"loud"`);
  - `Settings_menu_lists_sound_music_and_effects_after_text_size`;
  - `Right_on_music_volume_raises_it_and_stops_at_10`;
  - `M_in_play_toggles_sound_and_saves`;
  - `M_on_the_title_toggles_sound`;
  - `M_on_the_initials_screen_types_an_M_and_leaves_sound_alone`;
  - `ToggleSound_never_reaches_the_game`: the world's `StateHash` is unchanged.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run `dotnet test`.** All tests pass. Existing tests that list the settings rows are updated to
  include the three new rows, and nothing else in them changes.
- [ ] **Step 5: Commit** `feat(audio): sound settings, settings rows and the M key`

### Task 6: Music data and slot rules

**Files:**
- Modify: `src/NightPursuit.Core/Missions/MissionDefinition.cs` (`MissionMusic? Music = null`),
  `MissionLoader.cs` (the optional `music` block)
- Create: `src/NightPursuit.Core/Audio/MusicManifest.cs`, `MusicSlots.cs`
- Create: `content/music/music.json`, with every slot set to `""`
- Create: `content/music/README.md`, a short owner how-to: which file goes where, WAV or MP3, the `volume` trim,
  loops should end cleanly, and the GitHub 100 MB limit per file
- Test: `tests/NightPursuit.Core.Tests/Audio/MusicSlotsTests.cs`, `MusicManifestTests.cs`; extend the mission
  loader tests

**Interfaces (produces):**
- `sealed record MusicTrack(string File, double Volume = 1)`.
- `sealed record MissionMusic(MusicTrack? Theme, MusicTrack? Boss)`.
  - Mission JSON is `"music": { "theme": <entry>, "boss": <entry> }`, where an entry is `"file.mp3"` or
    `{ "file": "...", "volume": 0.8 }`.
  - `volume` runs 0–2, and the file name is a plain name with no folders.
  - `""` means empty, and unknown fields are a `MissionFormatException` naming the field (as `boss` does).
- `sealed class MusicManifest`:
  - members: `MusicTrack? Title, Briefing, Results, GameOver, Finished`;
  - `static (MusicManifest Manifest, IReadOnlyList<string> Problems) Load(string musicDir)`;
  - a missing `music.json` is an empty manifest with no problem;
  - bad JSON is an empty manifest plus the problem `MUSIC LIST COULD NOT BE READ`.
- `enum MusicSlot { None, Title, Briefing, MissionTheme, BossTheme, Results, GameOver, Finished }`.
- `readonly record struct MusicCue(MusicSlot Slot, MusicTrack? Track, double Level, int Generation)`.
  - `Generation` goes up whenever the same slot must start again from the beginning (a new world).
- `sealed class MusicSlots(MusicManifest manifest)`:
  - `MusicCue Update(ScreenFlow flow)`: called once per frame;
  - `IEnumerable<string> FilesNamed(IEnumerable<MissionDefinition> missions)`: every file the manifest and the
    missions name, for the start-up check.
- **Rules (the design's §6.1):**
  - **Screens to slots:** Title → Title (falls back to Briefing). Briefing → Briefing. Play → MissionTheme, then
    BossTheme from the frame the world has published `BossAppeared` until the run leaves Play. MissionComplete →
    Results. GameOver → GameOver (falls back to Results). Finished → Finished (falls back to Results).
  - **Side screens** keep the slot and track they started with: Settings, Controls, QuitConfirm, HighScores and
    EnterInitials.
  - **Boss theme missing:** the mission theme carries on (the cue doesn't change).
  - **Level:** 0.4 while `Screen.Pause`, or a side screen opened from Pause other than Settings. Settings is always
    1.0. Everything else is 1.0.
  - **A new `World` instance** (a restart or the next mission) bumps `Generation`. A respawn keeps the same world,
    so the music carries on.
  - **Boss sighting is remembered per world**, read from `World.Events`, once per new `World.Time`. This keeps it
    correct across paused frames.

- [ ] **Step 1: Write failing tests**
  - `Title_uses_briefing_music_when_it_has_none`;
  - `Play_uses_the_mission_theme_then_the_boss_theme_after_BossAppeared`;
  - `Missing_boss_theme_keeps_the_mission_theme`;
  - `Game_over_and_finished_fall_back_to_results`;
  - `Settings_from_the_title_keeps_title_music_at_full_level`;
  - `Pause_drops_to_40_percent_and_settings_from_pause_is_full`;
  - `A_practice_restart_restarts_the_mission_theme`;
  - `A_respawn_keeps_the_music_going`;
  - `Loader_reads_music_as_a_name_or_an_object_with_volume`;
  - `Loader_rejects_a_music_file_with_a_folder_or_an_unknown_field`;
  - `Missing_music_json_is_empty_and_silent`;
  - `Bad_music_json_reports_one_problem`;
  - `FilesNamed_lists_every_named_file_once`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement. Create `music.json` and the README.**
- [ ] **Step 4: Run `dotnet test`.** All tests pass, and all eight mission files still load unchanged.
- [ ] **Step 5: Commit** `feat(audio): music slots, mission music and the music list`

### Task 7: The sound director

**Files:**
- Create: `src/NightPursuit.Core/Audio/SoundDirector.cs`
- Test: `tests/NightPursuit.Core.Tests/Audio/SoundDirectorTests.cs`

**Interfaces:**
- Consumes: `SoundCue`, `CuePlay` and `LiveState` (Tasks 2–4) and `MusicSlots` (Task 6).
- Produces:
  - `sealed record SoundFrame(IReadOnlyList<CuePlay> Cues, LiveState Live, MusicCue Music)`.
  - `sealed class SoundDirector(MusicSlots music)`: `SoundFrame Update(ScreenFlow flow, double realSeconds)`.
- **Event cues** follow the design's §5.2 table. They are read only when `World.Time` has changed since the last
  update (the same guard `EffectList.Absorb` uses), and they are reset on a new `World`.
  - The `EnemyMissileEnded` result picks `Explosion`, `EnemyMissileDodged` or `EnemyMissileShotDown`.
  - `PlayerBusted` in the same frame as `PlayerWrecked` plays only `PlayerBusted`.
  - Plain `Message` events give no cue.
- **Guns** are found the way `EffectList.Absorb` finds them: a player `Tracer` not seen before gives `GunPlayer`.
  A hostile tracer not seen before gives `GunEnemy`, from its owner's position.
- **Distance and side** for cues from another car (by `VehicleId` or `OwnerId`; a car already gone uses its last
  known position, or plays centred):
  - volume = 1 / (1 + (d / 60)²), where d is the distance along the road in metres;
  - silent beyond 400 m;
  - pan = clamp((car.Lateral − player.Lateral) / 10, −1, 1). Check the sign against `Projector` so that a car
    drawn on the right pans right, and pin it with a test.
- **Rate limits** (by real time): the same `WarningKind` is not repeated within 1.5 s. Otherwise a cue waits at
  least this long before repeating:

  | Cue | Minimum gap |
  |---|---|
  | `PlayerHit` | 0.25 s |
  | `BossHit` | 0.2 s |
  | `GunPlayer` and `GunEnemy` | 0.06 s |
  | `Explosion` | 0.08 s |
  | any other cue | 0.05 s |

  - `PlayerHit` volume = clamp(0.5 + amount / 40, 0.5, 1).
- **Live state** is silent unless the screen is Play and the run is `Playing` or `Respawning`. The engine is off
  while wrecked or respawning.
  - `Speed01` = player speed / `VehicleStats.Player.TopSpeed`. `Throttle01` = `Player.Throttle`.
    `Punctured` = `PunctureLeft > 0`.
  - `OffRoad01` = `OffRoad` ? min(1, speed / 30) : 0. `Skid01` = `GripLoss > 0` ? 1 : 0.
  - **Siren:** heat `Level` ≥ `Pursuit` and a police car driving within 400 m. Level and pan come from the
    nearest one, using the distance and side rules above.
  - **Whine:** the nearest enemy missile ahead or behind within 150 m. `WhineCloseness01` = 1 − d / 150.
- **Menu cues** come from comparing the flow with the last frame:
  - the selected row moved → `MenuMove`;
  - the screen changed after a Confirm → `MenuConfirm`;
  - the screen changed after a Back → `MenuBack`;
  - a settings value changed → `MenuConfirm` (so an effects-volume change is heard at the new level);
  - the initials text changed → `MenuLetter`.

  The App tells the director which menu key it passed this frame, through `void NoteMenuKey(MenuKey k)`.
- **Music:** `MusicSlots.Update(flow)` is passed through in `SoundFrame.Music`.

- [ ] **Step 1: Write failing tests** (drive a real `ScreenFlow` with `MemoryGameStore`, and place cars and events
  the way the existing world tests do):
  - `Each_event_in_the_table_gives_its_cue` (a theory over the §5.2 rows);
  - `Paused_frames_do_not_replay_the_last_events`;
  - `A_new_world_resets_seen_tracers_and_stops_live_sounds`;
  - `Busted_plays_once_without_the_wreck_sound`;
  - `A_repeated_warning_within_1_5_s_is_silent`;
  - `Explosion_400_m_away_is_silent_and_60_m_away_is_half`;
  - `A_car_on_the_right_pans_right`;
  - `New_player_tracer_gives_one_gun_cue`;
  - `Siren_only_at_pursuit_with_police_near`;
  - `Engine_off_on_screens_and_while_wrecked`;
  - `Menu_move_and_confirm_give_menu_cues`;
  - `Director_never_changes_the_state_hash` (a 30 s autopilot-style drive with the director called every frame
    gives the same `StateHash` as without it).
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run `dotnet test`.** All tests pass.
- [ ] **Step 5: Commit** `feat(audio): sound director (events, guns, live state, menus, music)`

### Task 8: The App: NAudio output, music decoding and wiring

**Files:**
- Modify: `src/NightPursuit.App/NightPursuit.App.csproj` (`<PackageReference Include="NAudio" Version="3.3.0" />`)
- Create: `src/NightPursuit.App/Audio/AudioOutput.cs`, `MusicDecoder.cs`, `SoundSystem.cs`
- Modify:
  - `App.xaml.cs`: create the `SoundSystem` after the flow; post the start-up problems as notices;
  - `GameLoop.cs`: one `SoundSystem.Update` per frame after `_flow.Update`, plus `NoteMenuKey` for each menu key
    handed to the flow;
  - `MainWindow.xaml.cs`: dispose the sound system on close;
  - `Rendering/GameView.cs` and `Hud.cs`: the `SoundOff` mark.
- Create: `THIRD-PARTY-NOTICES.md`: NAudio (MIT licence, with its copyright line) and its dependency
  `System.Numerics.Tensors` (MIT)
- Test: Core tests only (the App has no test project). Verify with `--snapshot` and one interactive run.

**Interfaces:**
- **`AudioOutput : IDisposable`:**
  - It plays the `AudioMixer` (already an `ISampleProvider`) through NAudio.WinMM's `WaveOutEvent`
    (`DesiredLatency = 80`, `NumberOfBuffers = 3`).
  - `WaveOutEvent` is confirmed present in NAudio.WinMM 3.3.0 on this PC. `AudioFileReader` and
    `MediaFoundationReader` should be checked against the package's XML docs before use.
  - If 3.3.0 names these differently, follow the package's XML docs and log the difference.
  - `static AudioOutput? TryStart(AudioMixer m, out string? problem)`: if there's no device or `Init` or `Play`
    throws, it returns null with the problem `SOUND IS OFF: NO SOUND DEVICE FOUND` (or `SOUND IS OFF: ` + a short
    reason).
  - `PlaybackStopped` with an exception sets `Lost`, and the next `SoundSystem.Update` posts
    `SOUND STOPPED: THE SOUND DEVICE WENT AWAY` once. There is no reconnect; restarting the game brings sound back.
    The design left this to the builder, so log it as a decision.
- **`MusicDecoder : ISampleProvider, IDisposable`:**
  - It opens a WAV or MP3 with NAudio's `AudioFileReader` (float samples, with a `Volume` property for
    `MusicTrack.Volume`). It then adds `MonoToStereoSampleProvider` for a mono file, and
    `WdlResamplingSampleProvider` when the rate isn't 44.1 kHz.
  - It loops from the start at the end. This part is *ours*: a few lines that reset the reader's `Position` to 0
    when a read comes back short.
  - `static MusicDecoder? TryOpen(string path, out string? problem)` gives the problems
    `MUSIC FILE NOT FOUND: NAME` and `MUSIC FILE COULD NOT BE READ: NAME`.
- **`SoundSystem : IDisposable`:**
  - It owns the `SoundBank`, `AudioMixer`, `AudioOutput`, `SoundDirector` and `MusicSlots`.
  - Startup checks every file in `MusicSlots.FilesNamed(...)` once and posts one notice per problem via
    `flow.ShowNotice`.
  - `Update(ScreenFlow flow, double seconds)`:
    - copies the settings into the mixer (`SoundOn`, the volumes);
    - plays the frame's cues and sets the live state;
    - when the `MusicCue` changes (slot, track or generation), opens the new track and calls `SetMusic`, with a
      2.0 s fade into `BossTheme` and 1.5 s otherwise;
    - sets `MusicLevel`.
  - It works with no output (`AudioOutput` null): it still runs the director, so `--perf` can time it.
  - Music files open on the game thread at slot changes only. A slow open happens on a screen change, never
    mid-play, except at `BossAppeared` (acceptable; log it).
- **SOUND OFF mark:** `GameView.SoundOff` (set from `!flow.Settings.SoundOn`) draws `SOUND OFF` in the HUD's small
  text style, in a corner that is free at every snapshot size and text size. The builder picks the corner and logs
  it.
  - Add a `--snapshot` scene `sound-off` to `DiagnosticsRunner.Scenes`.
  - It shows in play only, since menus already show the setting.

- [ ] **Step 1: Write the Core-side failing tests for anything new in Core** (if `SoundSystem` logic is thin,
  there may be none). Otherwise go straight to the wiring.
- [ ] **Step 2: Implement the package reference, the three classes and the wiring.**
- [ ] **Step 3: Build.** `dotnet build -c Release` gives 0 warnings and 0 errors, and `dotnet test` passes.
- [ ] **Step 4: Snapshot check (one exe launch).** Run
  `--snapshot out.png --scene default,sound-off --size 960x540,320x200,2560x600 --text-size normal,largest`.
  The mark shows, overlaps nothing, and the default scene has no mark.
- [ ] **Step 5: Commit** `feat(audio): NAudio output, music decoding, sound system wiring and SOUND OFF mark`

### Task 9: `--sound-check`, `--perf` sound timing, and the docs

**Files:**
- Modify: `src/NightPursuit.App/Diagnostics/DiagnosticsRunner.cs` (add `--sound-check` to the diagnostics list in
  `App.xaml.cs` too)
- Modify: `docs/decisions.md`: the D151+ entries, an "Open items after Plan 5" section, and a "Checks only you can
  do (sound)" list taken from the design's §12, plus the ear checks the tests can't make
- Test: run the diagnostics.

**Interfaces:**
- **`--sound-check out.wav [--mission mission-01] [--seconds 60]`:**
  - It drives the mission with `Autopilot` at 1/60 s steps, through a `ScreenFlow` practice game started on Play.
  - Each frame it calls `SoundSystem.Update` with no output, then pulls 1/60 s of samples (735 stereo frames) from
    the mixer into a stereo WAV (written with NAudio's `WaveFileWriter`, through `SoundFiles.WriteStereo16`).
  - It writes `out.log`: one line per cue and music change, `t=12.35 GunPlayer vol=1.00 pan=0.00`.
  - It reports in the diagnostics log:
    - the peak in dBFS;
    - the number of samples over the ceiling (it must be 0);
    - the cue counts by kind;
    - the mixing time against the audio length.
  - It exits with 1 if any sample is over the ceiling, any cue's file is missing, or the mixing time is ≥ the audio
    length.
- **`--perf`:** a second timed line. The mean and p95 of `SoundSystem.Update` (no output) over the same frames,
  with a budget mean under 0.5 ms. The pass needs both budgets.

- [ ] **Step 1: Implement both.**
- [ ] **Step 2: Run** `--sound-check sc.wav --mission mission-01 --seconds 90` (one exe launch). It exits 0, the
  log shows guns, explosions, warnings, the engine running, and the boss cue if the boss is reached. Paste the
  summary into the decisions log.
- [ ] **Step 3: Run `--perf`** (one exe launch). The render mean is under 8 ms and the sound mean under 0.5 ms.
  Record both in the decisions log.
- [ ] **Step 4: Write the decisions-log sections.**
- [ ] **Step 5: Commit** `feat(audio): --sound-check diagnostic, perf sound timing, plan 5 decisions`
