# Night Pursuit — Sound and Music Design

Date: 2026-11-21. Follows the main design (`2026-11-17-night-pursuit-design.md`, §7.6 Events and §9 Sound)
and builds on everything on `main` (plans 1–4 and the line-art restyle). Decisions in this design continue
the numbering in `docs/decisions.md` from **D151**.

## 1. Intent

Night Pursuit gets sound effects and music. The owner wants them for themselves, for family watching them play,
and for other people who may play the game later. Sound should make the game feel like a fast, dark 80s night
drive and help someone watching follow the action.

The owner is hard of hearing. The main design's rule still holds and is binding here: **sound only repeats what
the screen already shows** (NFR-3). Nothing new is shown only by sound, and the game plays fully with sound
off. The owner ruled that there are **no hearing-specific options** (no balance, mono or similar): one ordinary
mix for everyone.

## 2. Goals, non-goals, anti-goals

**Goals**
- Sound effects in an **80s night-drive synth** style (owner's choice): deep pulsing synth bass, punchy
  electronic hits, wailing synth sirens. All made by us in code, so there is nothing to license.
- Music supplied by the owner (they are composing it). The game plays their files in named slots.
- Distinct sounds for different kinds of moment, so a watcher can tell a hit from a pickup from a warning.
- Simple controls: sound on/off, music volume, effects volume, and a mute key.
- The game never fails because of audio: no sound device, a bad file or a missing file means silence and an
  on-screen notice, never a crash.

**Non-goals**
- Hearing-specific options (owner ruling).
- Composing music (the owner supplies it).
- Spoken voice, true 3D sound or surround sound.
- Music that responds within a piece beyond the mission-to-boss switch below (no layers that follow the heat level).
- Sound out of the speakers during the diagnostics runs (`--snapshot`, `--stress`, `--perf` and `--sound-check`
  never play anything; §9 says what `--perf` and `--sound-check` measure).

**Anti-goals** (a failure even if it works)
- Any information that only sound carries.
- Tiring sounds: an engine drone or a repeated warning that wears on the ear over a 10-minute mission.
- Sounding like a copy: no sound-alike of the theme music of the arcade game that inspired this, or of any other
  existing game. This also applies to effects.
- Audio that drops frames: the 8 ms per frame render budget (`--perf`) must still hold.
- Clicks, pops or clipping caused by the mixing itself.

## 3. Approaches considered

1. **Everything pre-made as files.** A tool builds each synth sound into a WAV file and the game plays the files.
   Easy to listen to and to swap, but steady sounds such as an engine that follows speed come from speeding up a
   loop, which sounds less natural.
2. **Everything synthesised live in the game.** Steady sounds follow the game exactly, but each sound is harder
   to listen to on its own or replace, and live audio code is fussy: small mistakes come out as clicks.
3. **Mix of the two (chosen by the owner).** One-off sounds are WAV files made by a generator tool. The few
   steady sounds (engine, off-road rumble, police siren, incoming-missile whine) are made live from a handful of
   values the game sets each frame.

All three use **NAudio** (MIT licence, NuGet) for output and mixing. WPF's own `MediaPlayer` is poor at playing
many short overlapping sounds. NAudio is the project's first outside package.

## 4. How it fits the existing code

- **One-off moments** already arrive as `GameEvent`s (`World.Events`, the events since the last `Advance`). The
  sound layer reads the same list the HUD reads, once per frame.
- **Steady sounds** read the world's state each frame: player speed and throttle, `OffRoad`, `GripLoss`,
  `PunctureLeft`, police positions and the heat level, and enemy missiles in flight.
- **Guns have no event** (only oil, smoke, caltrops, missile and strike publish `WeaponUsed`). Gun sounds come
  from the gun-firing state each frame, for the player and for enemy Gunners. The builder may instead add a
  presentation-only event to Core. Either way it must not change `StateHash` or how the game plays.
- **Screens** come from `ScreenFlow.Current` (`Screen` enum), which drives the music.
- **Core uses only NAudio.Core** (plain .NET, with no Windows or WPF parts). The decisions (which cue, which music
  slot, rate limits, how loud and which side for a far-away car) and the mixer graph live in Core, with unit tests.
  The output device and MP3 decoding live in the App. *(Revised 2026-11-21 at the owner's prompting: build on
  NAudio's own mixer, effects, limiter and WAV code instead of writing our own. The build plan uses NAudio 3.3.0.)*

Proposed layout (the builder may adjust names):
- `src/NightPursuit.Core/Audio/`: `SoundCue` (the list of effect names), `SoundDirector` (events + state +
  screen → cues to play, steady-sound parameters, music slot), `MusicSlots` (which slot each screen and moment
  uses, with fallbacks), `MusicManifest` (reads the list file), rate limiting and distance/pan maths.
  - Also in Core: the mixer graph built from NAudio.Core blocks (one stereo float mix at 44.1 kHz, a final
    limiter), `SoundBank` (loads `content/sounds/*.wav` into memory once), and the live voices (the steady synth
    sounds).
- `src/NightPursuit.App/Audio/`: the output device, the music file decoder (looping), and the `SoundSystem` that
  ties it all to the game loop.
- `tools/SynthSounds/`: a console project that writes every one-off WAV into `content/sounds/`. It is
  deterministic: the same code gives the same files. The generated WAVs are committed, so building or running
  the game never needs the tool.

## 5. Sound effects

### 5.1 Style
The 80s night-drive synth style: analogue-style oscillators (saw, square, pulse, sine), filtered noise, short
pitch sweeps, and gated, punchy envelopes. Bass-led. Bright, piercing high tones are kept for the few urgent
warnings, so they stand out.

### 5.2 One-off sounds (WAV files)
Each row repeats something already on screen (the last column), as NFR-3 requires.

| Moment (source) | Sound | Already shown by |
|---|---|---|
| Player guns firing (state) | short punchy synth "thunk" at the fire rate | tracers, muzzle flash |
| Enemy Gunner firing (state) | higher, thinner burst, from the Gunner's side | Gunner's firing lines |
| Oil / smoke / caltrops dropped (`WeaponUsed`, player or enemy) | oil: low wet "blorp"; smoke: hiss burst; caltrops: metallic scatter | the hazard on the road, the weapon panel |
| Player missile fired (`WeaponUsed` Missile) | rising whoosh | the missile, the weapon panel |
| Satellite strike called / landed (`WeaponUsed` Strike, `StrikeLanded`) | radio blip, then a descending zap and a deep boom | the strike effect |
| Car destroyed (`VehicleDestroyed`) | explosion: noise burst plus a low thump, scaled by distance | the wreck or burnt-out car, the score message |
| Player hit (`PlayerDamaged`) | metallic crunch, louder for bigger hits | armour bar, screen shake, windshield cracks |
| Player wrecked or busted (`PlayerWrecked`, `PlayerBusted`) | big long explosion, or a siren stab and a low falling chord; the engine cuts out | the wreck and busted screens |
| Puncture (`VehiclePunctured`) | pop and hiss | the puncture bar and wobble |
| Spin (`VehicleSpun`) | tyre squeal | the spin |
| Supply collected (`SupplyCollected`) | rising arpeggio; armour gets a deeper chord | the pickup message, the weapon panel |
| Warning appears (`WarningRaised`) | short two-note alert; an urgent alarm for `MissileIncoming` | the warning text |
| Boss appears / changes phase / is hit (`BossAppeared`, `BossPhaseChanged`, `BossDamaged`) | deep synth-brass stab / shorter stab / metallic clank | boss bar, boss messages |
| Enemy missile launched / ends (`EnemyMissileLaunched`, `EnemyMissileEnded`) | ignition; then an explosion (hit), a whoosh past (dodged) or a small pop (shot down) | the missile and its warning |
| Heat level up / down (`HeatLevelChanged`) | a siren "whoop" / a falling tone | heat meter |
| Speed trap (`SpeedTrapPassed`) | camera-flash zap | the flash and message |
| Roadblock hit (`RoadblockHit`) | crash | the roadblock, the message |
| Menu move / confirm / back, initials letter | soft tick / blip / lower blip / tick | the highlighted menu line |

Plain `Message` events have no sound of their own: they nearly always come with one of the events above.

### 5.3 Steady sounds (made live)
- **Engine:** two slightly detuned oscillators plus a sub-bass sine, pitch following speed. Throttle opens a
  low-pass filter, so pressing the gas brightens the sound. It idles when stopped and wobbles while punctured.
  It sits low in the mix, so it is felt more than heard.
- **Off-road rumble:** filtered noise, louder with speed, while `OffRoad`.
- **Skid:** a squeal while `GripLoss` (oil) is counting down.
- **Police siren:** a two-tone synth wail while police are chasing. It comes from the side of the nearest police
  car and is louder as it gets closer. It plays at heat levels where the police pursue, not for patrols that
  are just driving by.
- **Incoming missile:** a whine that rises in pitch as an enemy missile closes in.

### 5.4 Distance, side and limits
- Sounds from other cars come from their side (left/right pan by their offset from you) and get quieter with
  distance. They are silent beyond about 400 m. The builder tunes the curve.
- **Rate limits** stop pile-ups: the same warning cannot repeat within about 1.5 s, and damage crunches and boss
  clanks are spaced out. At most about 24 sounds play at once; the quietest is dropped first.
- A final limiter stops the mix from clipping.

## 6. Music (the owner's files)

### 6.1 Slots

| Slot | Plays on | If the slot is empty |
|---|---|---|
| Title | Title screen | the Briefing track |
| Briefing | every Briefing screen | silence |
| Mission theme | play, for each mission (mission-01 … 06, proving-ground, patrol-ground) | silence |
| Boss theme | from `BossAppeared` until the mission ends, for each mission | the mission theme carries on |
| Results | Mission Complete | silence |
| Game over | Game Over | the Results track |
| Finished | Finished (campaign complete) | the Results track |

- **Side screens keep the current music.** Settings, Controls, Quit Confirm, High Scores and Enter Initials
  carry on with whatever was playing (the title music from the title, the results music after a game).
- **Pause** keeps the mission or boss music playing, quieter (about 40%). Steady sounds stop. Losing window
  focus pauses the game, so it does the same. **While SETTINGS is open, the music plays at full level**, even from
  the pause menu, so volume changes are heard as they really are. It drops back to 40% on returning to the pause
  menu. *(My call.)*
- **Respawn** keeps the music going. A practice **restart** (R) starts the mission theme again from the beginning.
- **Cross-fades:** about 1.5 s between screens, about 2 s into the boss theme. Each track loops until its slot
  ends, so the owner's loops should end cleanly.

### 6.2 Where the files go
- Files: WAV or MP3, in `content/music/`.
- **Mission and boss themes are named in the mission's own file**, so a new mission brings its own music.
  It is an optional block:
  ```json
  "music": { "theme": "glass-canyon.mp3", "boss": "red-lantern.mp3" }
  ```
- **Screen themes** are named in `content/music/music.json`:
  ```json
  { "title": "", "briefing": "briefing.mp3", "results": "results.mp3", "gameOver": "", "finished": "" }
  ```
- Each entry may instead be `{ "file": "results.mp3", "volume": 0.8 }`. That lets the owner even out loudness
  between tracks without re-exporting them.
- **A named file that is missing or can't be read** gives one on-screen notice at start-up, naming the file
  (shown the same way the settings-load problems are shown today). That slot is then silent. Empty slots are
  normal and give no notice.
- Until the owner's tracks arrive, the game ships with every slot empty and works silently for music.
- **The owner's music files are committed to the repo** (owner ruling, 2026-11-21), so every checkout and release
  has them. They are not git-ignored, and mission files on `main` name only tracks that are committed. If the
  repo is ever made public, the tracks become downloadable with it. GitHub refuses single files over 100 MB.

## 7. Settings and controls

- Three new lines in SETTINGS: **SOUND** ON/OFF, **MUSIC VOLUME** 0–10, **EFFECTS VOLUME** 0–10.
  Left and right change them, like the existing lines. Defaults: sound on, music 7, effects 8.
- They are saved with the other settings (`GameSettings`, settings JSON in %AppData%\NightPursuit). Older settings
  files without them load with the defaults.
- **M** turns sound on and off during play and on every screen except Enter Initials (where letters type).
- While sound is off, a small **SOUND OFF** mark shows in a corner of the play screen. Volume changes are heard at
  once (an effects change plays a sample blip).
- The controls help screen lists M.
- Sound starts **on** for a new player, as most games do.

## 8. Failure handling

- No audio device, or NAudio fails to start: the game runs silently and shows a notice once ("Sound is off: no
  sound device found" or the reason).
- The device goes away mid-game (headphones unplugged): playback stops quietly. The builder decides whether to
  reconnect. It must never crash or freeze the game.
- A missing or bad WAV in `content/sounds/`: that cue is silent. The tests check every cue has its file.

## 9. Checking it without listening

The builder (and R. Claude) can't judge the sound by ear, so checks are measurements. The owner and family judge
how it actually sounds.

- **Unit tests (Core):** each event and state gives the right cue; rate limits; distance and pan maths; music slot
  choice for every screen and moment, including fallbacks; manifest reading (good, missing and bad files); settings
  defaults and loading an older settings file.
- **Generator report:** `tools/SynthSounds` writes `content/sounds/report.txt` with each sound's length, peak level
  and loudness, and checks that none clip. Optionally it writes a picture sheet of the waveforms.
- **`--sound-check out.wav --mission N --seconds S`:** a new diagnostic that runs the mission on autopilot (like
  `--snapshot`), mixes the audio offline into a WAV file without playing it, and writes a log of every cue and
  music change with its time. It proves sounds fire at the right moments and the mix doesn't clip. The window
  stays closed.
- **`--perf`** keeps timing the render as today (mean under 8 ms). It also times the per-frame sound work on the
  game's thread (the `SoundDirector` plus updating the live sounds), with the audio built against no output
  device, and reports it on its own line. That work must stay under 0.5 ms a frame on average. Mixing runs on
  NAudio's own thread and is covered by `--sound-check` finishing in less time than the audio it mixes.
  *(Calls I made: the audio engine must be able to run with no device, which `--sound-check` needs anyway; the
  0.5 ms figure keeps sound to a small slice of the frame.)*

## 10. Credits and licences

- NAudio: MIT licence, noted in a `THIRD-PARTY-NOTICES` file.
- Sound effects: made by this project's generator. The owner may swap any WAV in `content/sounds/` for their own
  file with the same name.
- Music: the owner's own work.

## 11. Left to the builder

- Exact synth recipes, lengths and mix levels for each sound.
- Exact rate-limit times, voice limit and distance curve.
- Whether guns get a presentation-only event or are read from state.
- Class and file names beyond the layout in §4.
- Whether to reconnect after the sound device goes away.

## 12. Owner checks (by ear, after the build)

- The overall feel: does it sound like an 80s night drive?
- The engine is not tiring over a whole mission.
- Warnings are noticeable but not irritating.
- Watchers can tell the main moments apart by sound.
- The owner's music tracks cross-fade and loop cleanly, and the boss switch lands well.
