# Night Pursuit Plan 4: Six Missions, Screens, Settings and High Scores. Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the single mission into the whole game:
- a six-mission campaign that carries your score and spare cars from mission to mission;
- a briefing before each mission, with the story, the boss and its drawing, the loadout, and the traffic and police;
- a mission bonus;
- respawning a short way back;
- the Title, Pause, Settings, Controls, Mission complete, Game over, High scores and initials screens;
- settings and the top-10 high scores saved in `%AppData%\NightPursuit`;
- a text-size setting.

Everything works from the keyboard or the gamepad, and nothing depends on sound.

**Architecture:** The rules of the session go in a new Core folder, `Session/`, so the tests can drive them:
- `Game` is one play-through: the mission list, score and spare cars, and the mission bonus.
- `ScreenFlow` decides which screen is showing and what each key does there.
- `MenuModel` and `InitialsEntry` are the menu and initials models.
- `GameSettings`, `HighScoreTable` and `FileGameStore` hold the settings and scores and save them as JSON.

`MissionRun` gains a `RunStart` (the score, spare cars and throttle scheme carried in) and respawns 150 m back.

The App gets passive XAML screens (spec §8.2) drawn over the `GameView`:
- one `ScreenHost` shows the view for `ScreenFlow.Current`;
- `GameLoop` sends key presses to the flow (on a screen) or to the game (in play).

The `--snapshot` diagnostic gains `--screen` and `--text-size` so every screen can be checked without screen capture.

**Tech Stack:** C# / .NET 10, WPF (`net10.0-windows`), xUnit 2.9.3, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-11-17-night-pursuit-design.md`:
- §5.7 MSN-1 to MSN-5;
- §5.4 ENM-4;
- §5.6 HT-5;
- §5.8 HUD-1 and HUD-5;
- §5.9 NFR-3 to NFR-5;
- §7.5, §8.2, §8.4 and §12.

Also read `docs/decisions.md`:
- D12 (focus pause), D13 (R only when paused or over);
- D44 (enemy missiles), D46 (proving ground), D47 (mission 1's boss);
- D58/D61 (what a respawn clears);
- D66 (help box), D68 (Tab);
- D96 (no point penalties for civilians: the owner's ruling, which overrides MSN-5's "penalties for civilians");
- D116 (stress-test length);
- "Open items after Plan 3".

**The four plans:**
1. Foundation and playable slice (built, branch `plan-1-foundation`)
2. Full arsenal, all six enemy types and the boss (built, branch `plan-2-arsenal-and-bosses`)
3. Civilians, police, heat and busted (built, branch `plan-3-traffic-and-police`)
4. **Six missions, all screens, settings, high scores, persistence (MSN, HUD-5, NFR-3/4)** (this plan)

## Global Constraints

- **Frameworks:**
  - Core and Tests target `net10.0`.
  - App targets `net10.0-windows` with `<UseWPF>true</UseWPF>`.
  - `Directory.Build.props` is unchanged.
  - No new NuGet packages; JSON uses `System.Text.Json`.
- **Core purity:** Core references no WPF types and never calls `DateTime.Now`, `Stopwatch`, `new Random()` or `Environment.GetFolderPath`. The App passes the data folder in.
- **Bit-for-bit:** a `MissionRun` built without a `RunStart`, which never respawns, gives exactly the same results as before this plan. Only a respawn changes (MSN-3, Task 2). Every existing test keeps passing. An existing test may change only in the cases a task names (Task 1: the boss field-list message; Task 2: tests that rely on the old respawn position), and the implementer lists each changed test in the report.
- **The real data folder:** the tests and the diagnostics never read or write `%AppData%\NightPursuit`. Tests use a fresh temporary folder per test (deleted after). Diagnostics use `MemoryGameStore`.
- **Saving never crashes the game.** A failed load or save becomes a one-line notice on screen. An unreadable file is copied to `*.bad.json` before anything overwrites it.
- **Nothing important depends on sound or colour alone (NFR-3):**
  - the selected menu item has a `>` marker and colour;
  - the initials cursor letter has brackets and an underline;
  - a new high-score row says `NEW`.
- **Original work only:** no names, cars, story, art or music from Spy Hunter, Speed Freak or any other game. The mission and boss names below are original.
- **No screen-capture scripts.** Visual checks use the app's `--snapshot`, `--stress` and `--perf` only.
  - Launch the freshly built exe as few times as possible (antivirus): one `--snapshot` run per task, with all its screens or scenes in that one run.
  - If the permission system refuses a launch, do not retry. Report what is left unseen.
- **Paths:** the App exe is `src/NightPursuit.App/bin/Release/net10.0-windows/NightPursuit.exe`, built with `dotnet build src/NightPursuit.App -c Release`. Snapshots go in `snapshots/` (git-ignored).
- **Commits:**
  - local commits on a new branch `plan-4-missions-and-screens`, created from `line-art` (the restyle branch, which holds Plan 3);
  - messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`;
  - never push.
- **Test command** (from the repo root): `dotnet test tests/NightPursuit.Core.Tests`. The baseline on `line-art` is 873 tests, so the total is 873 plus this plan's own.
- **Screen copy is upper case**, in the HUD's Consolas look. Exact strings are given in the tasks. Where a task gives a format such as `{score:00000}`, use it as written.

## Review Focus

1. **A score or a setting is lost.** Expected: the game starts with defaults, shows a one-line notice, keeps the old file as `*.bad.json`, and never crashes. Cases:
   - the files are missing, empty, not JSON, hold out-of-range values, or are locked;
   - the folder can't be written;
   - you quit mid-game with a qualifying score: it still reaches the initials screen.

   *(Task 4: `Unreadable_high_scores_are_kept_as_bad_and_a_new_table_starts`, `Out_of_range_settings_fall_back_per_field`, `A_failed_save_reports_a_problem_and_does_not_throw`. Task 5: `Quitting_with_a_qualifying_score_still_asks_for_initials`.)*
2. **A dead end or a stray key.** Expected:
   - every screen has a way forward and a way back from both keyboard and pad;
   - Esc never ends a game without the Quit confirmation;
   - a key that opens a screen never also acts on the game (Enter on the briefing doesn't fire; Esc that resumes doesn't pause again);
   - losing focus on any screen changes nothing but pausing play.

   *(Task 5: `Every_screen_leads_back_to_title_or_play`, `Esc_in_play_pauses_and_Esc_in_pause_resumes_once`, `Quit_needs_confirmation`, `Focus_lost_only_matters_in_play`. Task 7: GameLoop routes each frame's keys to one side only, and its fire latch keeps Space or pad A from firing when they confirm a screen.)*
3. **Carry-over goes wrong between missions.** Expected:
   - the score and spare cars carry over;
   - the bonus is added once;
   - armor and the loadout start fresh, and heat starts at the next mission's own value (HT-5);
   - game over in mission 4 records "mission 4";
   - finishing records "all 6".

   *(Task 3: `Score_and_spares_carry_and_bonus_is_added_once`, `Each_mission_starts_with_full_armor_its_own_loadout_and_heat`, `Game_over_records_the_mission_reached`.)*
4. **Respawning back puts you into trouble.** Expected:
   - you come back 150 m before the crash on a clear road: no wreck, slick, caltrops, roadblock or car within 200 m behind you through to 200 m past the crash;
   - a speed trap you already tripped doesn't count again;
   - waves are not sent again;
   - crashing near the start puts you at 0;
   - crossing back over the boss line keeps the boss stage.

   *(Task 2: all five named there.)*
5. **The largest text in a small window.** Expected:
   - screens shrink to fit instead of cutting text off;
   - HUD boxes grow with their text;
   - at 1280×720 with the largest text, no HUD text crosses its panel's edge.

   *(Task 10 snapshots at `--text-size largest`, at 1280x720 and 960x540. Task 11 stress covers every screen at every size and text size.)*

## Calls made in this plan

The spec leaves these open. Each is logged as a decision for the owner in Task 12, numbered from D131 (the line-art restyle used D117 to D130).

1. **Where things live.**
   - The session rules (`Game`, `ScreenFlow`, menus, initials, settings, high scores, saving) are in Core, so they are unit-tested.
   - The screens are passive XAML views (spec §8.2) with no focusable controls. Keys reach them only through the flow.
   - There is no mouse in menus (keyboard and pad only).
2. **What carries between missions.**
   - The score and spare cars carry over.
   - Each mission starts with full armor, the loadout its briefing shows (WPN-8) and its own starting heat (HT-5).
   - The throttle scheme comes from Settings; F2/F3 in play change it and save it.
   - No extra spare cars are awarded: 3 per game, as MSN-3 says.
3. **The mission bonus (MSN-5):**
   - the mission's own `bonus` (missions 1–6: 1000, 1500, 2000, 2500, 3000, 4000);
   - plus armor left × 10;
   - plus 1000 if you lost no car in that mission.

   It shows line by line on the Mission complete screen. There are no point penalties for civilians (D96).
4. **"A short way back" (MSN-3) is 150 m** before where you crashed (not where the wreck slid to), never before 0. The road is cleared from 200 m behind the new spot to 200 m past the crash: cars except the boss, and hazards except supply crates. Waves and crates are not rewound, a tripped speed trap stays tripped, and the boss comes back 300 m ahead of the new spot (as now).
5. **Restarting and quitting.**
   - No restarts in a campaign game: R does nothing, so you can't replay a mission with your spare cars back.
   - A practice game (`--mission name`) keeps R as before and has "RESTART MISSION" on its pause menu.
   - "QUIT TO TITLE" (from Pause or a briefing) asks for confirmation, then ends the game as "GAME ENDED". The score still goes to the high-score check.
6. **High scores (MSN-5):**
   - top 10;
   - a score qualifies if it is above 0 and the table has fewer than 10 entries or it beats the 10th;
   - an equal score ranks below the older one;
   - an entry is 3 initials A–Z, the score, the mission reached (the one you were on), and whether you finished all six (shown as `ALL 6`);
   - practice games are never recorded.
7. **Initials:**
   - type the letters, or use Up/Down to change the letter and Left/Right to move;
   - Backspace steps back and Enter saves;
   - the slots start with your last initials (`AAA` the first time);
   - Esc does nothing there, so a high score can't be thrown away by accident.
8. **Settings (HUD-5):**
   - **controls scheme:** "RAMP PEDAL + CRUISE" or "SPEED LEVER";
   - **lighter steering at speed:** on/off. This is `FeelSettings.SpeedSensitiveSteering`, default on;
   - **screen shake:** on/off, default on;
   - **text size:** Normal 100%, Large 125%, Largest 150%;
   - **key bindings:** a read-only view, the Controls screen.

   Changes apply at once and are saved when you leave Settings.
9. **Files (NFR-4):** `%AppData%\NightPursuit\settings.json` and `highscores.json`.
   - Each is written to a `.tmp` file first, then moved over the old one.
   - A file that can't be read is copied to `settings.bad.json` or `highscores.bad.json`, and defaults are used, with a notice.
   - If that copy fails, the file is never overwritten in this session, and a save reports why.
10. **F1 in play opens the Controls screen and pauses** (this closes the open item "F1 help doesn't pause the game"). F1, Enter or Esc closes it and play resumes. The start-of-run help box (gas to start, as now) shows only at the start of a game's first mission.
11. **The six missions** are in the table in Task 6. Missile-firing boss phases are only in missions 5 and 6 (ENM-4, D44): one phase each.
12. **Each boss has a look.** The JSON has `"look"`: `Fin` (today's shape, the default), `Twin` (two fins, wider) or `Dome` (raised cabin, no fins). It is used in play and for the briefing drawing (MSN-1). Colours stay the boss colour (ENM-1).
13. **The briefing's traffic and police words:**
    - **traffic:** 0 = NONE; up to 2 per km = LIGHT; up to 5 = MODERATE; above 5 = HEAVY;
    - **police presence:** none, or below 1 = LOW, exactly 1 = NORMAL, above 1 = HIGH;
    - the starting heat and its level are shown when they are above 0.
14. **The stress test (D116, the owner's decision is pending).**
    - It covers all 8 mission files.
    - At 960×540 it draws every 10 m (NFR-5). At 320×200 and 2560×600 it draws every 100 m, which is where the time went.
    - It also draws every screen at all three sizes and all three text sizes.
    - Estimated 40–45 minutes, against 68 for Plan 3. The owner can ask for the full cadence back.
15. **Practice missions** (`proving-ground`, `patrol-ground`, or any single mission) are reached only with `--mission name`. A practice game is that one mission: briefing, play, then "PRACTICE COMPLETE" or game over, then back to the title.

## File structure

**Core (new folder `src/NightPursuit.Core/Session/`):**
- `Campaign.cs`: the mission order.
- `Game.cs`, `GameStage.cs`, `MissionBonus.cs`: one play-through.
- `GameSettings.cs`: the settings model and `TextSize`.
- `HighScoreTable.cs`: `HighScoreEntry` and the table.
- `GameStore.cs`: `IGameStore`, `StoreResult`, `FileGameStore`, `MemoryGameStore`.
- `Menu.cs`: `MenuEntry` and `MenuModel`.
- `InitialsEntry.cs`.
- `MenuKey.cs`: `MenuInput` and `MenuKey`.
- `ScreenFlow.cs`: `Screen` and the flow.

**Core changes:**
- `Missions/MissionRun.cs`: `RunStart`, and respawning back.
- `Missions/MissionDefinition.cs`, `Missions/BossDefinition.cs`, `Missions/MissionLoader.cs`: the `bonus` and `look` keys.

**Content:** `content/missions/mission-02.json` to `mission-06.json`. `mission-01.json` is unchanged.

**App:**
- New `src/NightPursuit.App/Screens/`:
  - `NeonStyles.xaml`;
  - `ScreenHost.cs`;
  - `MenuScreen.xaml(.cs)`;
  - `BriefingScreen.xaml(.cs)`;
  - `ControlsScreen.xaml(.cs)`;
  - `ResultScreen.xaml(.cs)`;
  - `InitialsScreen.xaml(.cs)`;
  - `HighScoresScreen.xaml(.cs)`;
  - `BossPortrait.cs`.
- New `AppPaths.cs`.
- Changed: `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, `GameLoop.cs`, `Input/InputHub.cs`, `Input/XInputPad.cs`, `Rendering/GameView.cs`, `Rendering/HudLayout.cs`, `Rendering/Overlays.cs`, `Rendering/Hud.cs`, `Rendering/VehicleRenderer.cs`, and the other HUD renderers for text size.
- Diagnostics: new `Diagnostics/ScreenSnapshots.cs`; `DiagnosticsRunner.cs` changes only to dispatch to it and for the stress lists.

**Tests:** new `SessionGameTests.cs`, `GameStoreTests.cs`, `ScreenFlowTests.cs` and `CampaignTests.cs`. `MissionTests.cs` gains respawn and loader tests.

---

### Task 1: Mission file keys for the bonus and the boss's look

**Files:**
- Modify: `src/NightPursuit.Core/Missions/MissionDefinition.cs`, `src/NightPursuit.Core/Missions/BossDefinition.cs`, `src/NightPursuit.Core/Missions/MissionLoader.cs`
- Test: `tests/NightPursuit.Core.Tests/MissionTests.cs`

**Interfaces:**
- Produces:
  - `MissionDefinition.Bonus` (`int`): a new optional last record parameter `int Bonus = 1000`.
  - `public enum BossLook { Fin, Twin, Dome }` in `BossDefinition.cs`.
  - `BossDefinition` gains the optional last parameter `BossLook Look = BossLook.Fin`.
  - JSON keys: top-level `"bonus"` (an integer from 0 to 100000, default 1000) and boss `"look"` (`Fin`, `Twin` or `Dome`, case-insensitive, default `Fin`).

- [ ] **Step 1: Write the failing tests** in `MissionTests.cs`:
  - `Bonus_and_look_default_when_absent`:
    - `Load(Valid)` gives `Bonus == 1000`;
    - `mission-01.json` gives `Bonus == 1000` and `Boss.Look == BossLook.Fin`.
  - `Bonus_and_look_are_read`: add `"bonus": 2500` and a boss with `"look": "dome"` to `Valid`. Assert `2500` and `BossLook.Dome`.
  - `Bad_bonus_and_look_are_rejected`, asserting the exact messages:
    - `m.json: bonus must be a whole number from 0 to 100000`;
    - `m.json: boss.look must be Fin, Twin or Dome, not 'Round'`.
- [ ] **Step 2: Run** `dotnet test tests/NightPursuit.Core.Tests --filter MissionTests`. Expected: FAIL (no `Bonus`).
- [ ] **Step 3: Implement.** Read the keys with the reader's existing helpers, following the style of the existing messages.
  - The boss object already rejects unknown fields (MissionLoader.cs:126-128). Add `look` to the allowed names and to the bracket: `(name, health, phases, look)`.
  - Update the one matching `InlineData` in `BossMissionTests.Other_boss_and_wave_errors_name_the_field` (BossMissionTests.cs:175) to the new bracket, and name it in the report.
  - Reject digits for `look` the way `Kind()` does (MissionLoader.cs:283-285), since `Enum.TryParse("1")` would succeed.
- [ ] **Step 4: Run** the whole test project. Expected: PASS, including `Mission_01_loads` and `Patrol_ground_loads` unchanged.
- [ ] **Step 5: Commit** `feat: mission bonus and boss look keys`.

### Task 2: Carry-in values and respawning a short way back

**Files:**
- Modify: `src/NightPursuit.Core/Missions/MissionRun.cs`
- Test: `tests/NightPursuit.Core.Tests/MissionTests.cs` (respawn tests). Use `TestWorlds` and `MissionDefinition` built in code, as the existing respawn tests do.

**Interfaces:**
- Produces:
  - `public sealed record RunStart(int Score = 0, int SpareCars = 3, int SchemeIndex = 0)` in `MissionRun.cs`.
  - The constructor `MissionRun(MissionDefinition def, FeelSettings feel, int seed, RunStart? start = null)`. Existing callers compile unchanged.
  - `MissionRun.Start` (`RunStart`, never null).
  - `public const double RespawnBack = 150` on `MissionRun`.
  - `Rebuild()` applies the start values after making the World:
    - `World.Score = start.Score`;
    - `World.Player.SpareCars = start.SpareCars`;
    - if `start.SchemeIndex != 0`, then `World.Handle(new GameAction(ActionKind.SelectScheme, start.SchemeIndex))`.

    A practice restart (R) rebuilds with the same `Start`.

- [ ] **Step 1: Write the failing tests:**
  - `RunStart_carries_score_spares_and_scheme`: a start of `(4200, 1, 1)` gives `World.Score == 4200`, `Player.SpareCars == 1` and `World.SchemeIndex == 1`. After Pause then Restart, the same three values hold.
  - `Respawn_puts_you_150_m_back_on_a_clear_road`:
    - wreck at distance 3000 with a parked wreck hazard at 2900, an oil slick at 3150 and an enemy at 2880;
    - after the respawn the player's `Distance` is 2850;
    - no hazard (other than a crate) and no non-boss agent lies within `[2650, 3200]`.
  - `Respawn_near_the_start_stops_at_0`: wreck at 90; respawn at 0.
  - `A_tripped_speed_trap_does_not_count_again_after_respawn`:
    - put a `SpeedTrap` at 3240 with `Triggered = true` (one you had already passed), and wreck at 3000;
    - the cleared zone `[2650, 3200]` leaves the trap in place;
    - drive the new car from 2850 at 40 m/s past 3240, above its limit;
    - assert the heat is unchanged.
    - Control, in the same test: an untriggered trap at 3400 then adds 8, so the test can see a trap counting.
  - `Respawning_does_not_send_waves_again`:
    - a mission with waves at 500 and 1000 (`Def(approach: 4000, …)`, no traffic or police), wrecked at 1100;
    - record the set of `Agent.Id`s just before the respawn;
    - after the respawn plus 2 s of `ControlFrame.Empty`, no agent has an Id outside that set.
    - There is no spawn event, and the director's wave counter is private.
  - `Respawn_is_measured_from_the_crash_not_where_the_wreck_stops`: wreck at 3000 with `Player.Speed = 40`, so the wreck slides on. The respawn is at 2850.
  - `Crossing_back_over_the_boss_line_keeps_the_boss_stage`:
    - approach 1000 with a boss;
    - reach the boss stage, wreck at 1050, respawn at 900;
    - `Director.Stage == MissionStage.Boss` throughout;
    - exactly one `BossCar` exists, 300 m ahead of 900.
- [ ] **Step 2: Run** `dotnet test tests/NightPursuit.Core.Tests --filter MissionTests`. Expected: the new tests FAIL, except two that already pass on today's code and stay as guards: the speed-trap test (`CheckSpeedTraps` skips a triggered trap) and the waves test (the wave counter never rewinds). The speed-trap case is artificial: a trap you really passed is either inside the cleared zone or behind the new spot.
- [ ] **Step 3: Implement.**
  - In `OnWrecked()`, record `_crashAt = World.Player.Distance`. The wreck slides on for up to about 90 m during the 3 s respawn, so the crash point must be taken at the moment of the wreck.
  - In `Respawn()`, compute `double crash = _crashAt; double back = Math.Max(0, crash - RespawnBack);` and set `p.Distance = back` before `SnapPrev()`.
  - Replace the two `Math.Abs(… - p.Distance) < RespawnClearRadius` tests with "inside `[back - RespawnClearRadius, crash + RespawnClearRadius]`".
  - The boss return point uses `back`.
  - Add `RunStart` and `Start` as above.
- [ ] **Step 4: Run** the whole test project. Expected: PASS.
  - `BossMissionTests.Respawn_clears_hazards_where_the_boss_returns` (BossMissionTests.cs:368-389) puts its wreck where the boss used to return. Move the wreck to `p.Distance - MissionRun.RespawnBack + 300` and update its comment to say so.
  - If any other test relied on the crash-point position, change only that and name every changed test in the report.
- [ ] **Step 5: Commit** `feat: respawn 150 m back; carry score, spares and scheme into a run`.

### Task 3: `Game`, one play-through across missions

**Files:**
- Create: `src/NightPursuit.Core/Session/Campaign.cs`, `Game.cs`, `GameStage.cs`, `MissionBonus.cs`
- Test: `tests/NightPursuit.Core.Tests/SessionGameTests.cs`

**Interfaces:**
- Consumes: `RunStart` and `MissionRun` (Task 2), `MissionDefinition.Bonus` (Task 1), and `GameSettings` (Task 4).
  - To keep the tasks independent, this task creates `GameSettings` with only `public int SchemeIndex { get; set; }`. Task 4 adds the rest.
- Produces:
  - **The mission order and seeds:**
    - `public static class Campaign { public static readonly string[] MissionIds = { "mission-01", …, "mission-06" }; }`.
    - Each mission's World seed is `seed + MissionIndex`.
  - **Stages:** `public enum GameStage { Briefing, Playing, MissionComplete, GameOver, Finished }`.
  - **The bonus:**
    - `public sealed record MissionBonus(int Base, int ArmorLeft, bool NoCarLost)`, with:
      - `ArmorPoints => ArmorLeft * 10`;
      - `CleanPoints => NoCarLost ? 1000 : 0`;
      - `Total => Base + ArmorPoints + CleanPoints`;
      - `static MissionBonus For(MissionDefinition def, double armor, bool carLost)`, where `ArmorLeft = (int)Math.Floor(Math.Clamp(armor, 0, 100))`.
  - **`public sealed class Game`:**
    - `Game(IReadOnlyList<MissionDefinition> missions, FeelSettings feel, GameSettings settings, bool practice = false, int seed = 1)`. It throws `ArgumentException` for an empty list.
    - `IReadOnlyList<MissionDefinition> Missions`, `int MissionIndex`, `MissionDefinition Mission`, `bool Practice`.
    - `GameStage Stage`. It starts at `Briefing` for mission index 0.
    - `MissionRun? Run`: null in `Briefing`; it keeps the last run in `MissionComplete`, `GameOver` and `Finished`, so the frozen road can show behind the screen.
    - `int Score`: `Run.World.Score` while `Playing`, otherwise the carried value.
    - `int SpareCars`: likewise `Run.World.Player.SpareCars` while `Playing`.
    - `MissionBonus? LastBonus`.
    - `bool QuitEarly`.
    - `int MissionReached => MissionIndex + 1`.
    - `bool FinishedAll => Stage == GameStage.Finished && !Practice`.
    - `void StartMission()`: Briefing → Playing. It builds the run with `new RunStart(Score, SpareCars, settings.SchemeIndex)`.
    - `void Update(ControlFrame held, double seconds)`: only while `Playing`, it calls `Run.Update`, then:
      - if `Run.State == RunState.Completed`, it computes the bonus with `carLost = Run.World.Player.SpareCars < Run.Start.SpareCars`, sets `Score = Run.World.Score + bonus.Total`, carries the spare cars, and moves to `Stage = MissionComplete`;
      - if `Run.State == RunState.GameOver`, it carries the score and moves to `Stage = GameOver`.
    - `void Handle(GameAction a)`: while `Playing`, `Restart` is dropped unless `Practice`. Everything else goes to `Run.Handle`.
    - `void RestartMission()`: practice only, while `Playing`. It rebuilds the run through `Run.Handle(Restart)` when `Run.CanRestart`.
    - `void Continue()`: MissionComplete → the next Briefing (`MissionIndex++`, `Run = null`), or → `Finished` after the last mission.
    - `void Quit()`: from Briefing or Playing. It carries the score from the run if there is one, sets `QuitEarly = true` and `Stage = GameOver`.

- [ ] **Step 1: Write the failing tests** with two or three small in-code missions (approach 300 m, no waves, no boss, `Bonus` 1000 and 2000) on `TestWorlds`-style tracks. Drive with `ControlFrame` gas held (as in `MissionTests`), or set `Player.Distance` just short of the approach end.
  - `Score_and_spares_carry_and_bonus_is_added_once`:
    - mission 1 is completed with `World.Score = 700`, armor 64 and no car lost;
    - `LastBonus.Total == 1000 + 640 + 1000`;
    - `Score == 3340`;
    - the `Score` read again after another `Update` is still 3340;
    - after `Continue` and `StartMission`, `Run.World.Score == 3340` and `SpareCars == 3`.
  - `A_lost_car_costs_the_clean_bonus_and_carries`: wreck once in mission 1, then complete it. `NoCarLost == false` and `SpareCars == 2` in mission 2's run.
  - `Each_mission_starts_with_full_armor_its_own_loadout_and_heat`: mission 2 has police `startingHeat` 30 and a loadout of 2 missiles. End mission 1 with armor 10 and 0 missiles. In mission 2, the armor is 100, there are 2 missiles, and `World.Heat.Value == 30`.
  - `Game_over_records_the_mission_reached`: wreck four times in mission 2 of 3. `Stage == GameOver`, `MissionReached == 2`, and `Score` equals the run's score.
  - `Finishing_the_last_mission_reaches_Finished`: `Stage == Finished`, `FinishedAll` is true, `MissionReached == 3`.
  - `Restart_is_ignored_in_a_campaign_and_works_in_practice`: in a campaign game, pause then Restart, and `Run.World` (`Assert.Same`) and `World.Time` are unchanged. In a practice game, the same gives a new `World` (`Assert.NotSame`); `Rebuild` replaces the World, not the run object.
  - `Quit_ends_the_game_with_the_score_so_far`.
  - `The_scheme_comes_from_settings`: with `settings.SchemeIndex = 1`, the run starts with `World.SchemeIndex == 1`.
- [ ] **Step 2: Run** `dotnet test tests/NightPursuit.Core.Tests --filter SessionGameTests`. Expected: FAIL (no `Game`).
- [ ] **Step 3: Implement** `Game` as specified.
- [ ] **Step 4: Run** the whole test project. Expected: PASS.
- [ ] **Step 5: Commit** `feat: Game session across missions with mission bonus`.

### Task 4: Settings, high scores and saving them

**Files:**
- Create or complete: `src/NightPursuit.Core/Session/GameSettings.cs`, `HighScoreTable.cs`, `GameStore.cs`
- Test: `tests/NightPursuit.Core.Tests/GameStoreTests.cs`. Each test uses its own `Path.Combine(Path.GetTempPath(), "np-tests-" + Guid.NewGuid())` folder and deletes it in `Dispose`.

**Interfaces:**
- Produces:
  - **Settings:**
    - `public enum TextSize { Normal, Large, Largest }`.
    - `public sealed class GameSettings` with:
      - `int SchemeIndex` (0 or 1);
      - `bool LighterSteering = true`;
      - `bool ScreenShake = true`;
      - `TextSize TextSize = Normal`;
      - `string LastInitials = "AAA"`;
      - `double TextScale` (1.0, 1.25 or 1.5);
      - `GameSettings Clone()`.
  - **High scores:**
    - `public sealed record HighScoreEntry(string Initials, int Score, int MissionReached, bool Finished)`.
    - `public sealed class HighScoreTable` with `const int Size = 10`, `IReadOnlyList<HighScoreEntry> Entries` (best first), `bool Qualifies(int score)`, `int RankFor(int score)` (the 1-based rank a new score would take, or 0 if it doesn't qualify) and `int Add(HighScoreEntry e)` (returns that rank and inserts it).
    - An equal score goes below the existing ones, and the table never holds more than 10.
  - **Saving:**
    - `public sealed record StoreResult(bool Ok, string? Problem = null)`.
    - `public interface IGameStore { StoreResult SaveSettings(GameSettings s); StoreResult SaveHighScores(HighScoreTable t); }`.
    - `public sealed class FileGameStore : IGameStore` with `FileGameStore(string directory)`, `(GameSettings Settings, string? Problem) LoadSettings()` and `(HighScoreTable Table, string? Problem) LoadHighScores()`.
    - `public sealed class MemoryGameStore : IGameStore`, which counts the saves and keeps the last values.
  - **The files** are `settings.json` and `highscores.json` in that directory. The directory is created on the first save.
    ```json
    { "version": 1, "throttleScheme": "RampPedal", "lighterSteering": true, "screenShake": true, "textSize": "Normal", "lastInitials": "AAA" }
    { "version": 1, "scores": [ { "initials": "JMA", "score": 12345, "missionReached": 3, "finished": false } ] }
    ```
    `throttleScheme` is `RampPedal` or `SpeedLever`.
  - **Exact notice text:**
    - `SETTINGS FILE COULD NOT BE READ. USING DEFAULTS (OLD FILE KEPT AS settings.bad.json)`
    - `HIGH SCORES FILE COULD NOT BE READ. STARTING A NEW TABLE (OLD FILE KEPT AS highscores.bad.json)`
    - If the `.bad` copy fails, the bracket reads `(OLD FILE COULD NOT BE KEPT, SO IT WILL NOT BE OVERWRITTEN)`.
    - A failed save gives `COULD NOT SAVE SETTINGS: {reason}` or `COULD NOT SAVE HIGH SCORES: {reason}`, where `{reason}` is the exception's `Message`, cut to 80 characters.
    - A save refused because the old file couldn't be kept gives `COULD NOT SAVE HIGH SCORES: THE OLD FILE COULD NOT BE KEPT`, and likewise for settings.

- [ ] **Step 1: Write the failing tests:**
  - `Missing_files_give_defaults_without_a_problem`.
  - `Settings_round_trip` and `High_scores_round_trip`.
  - `High_score_table_keeps_the_best_10_and_ranks_ties_below`:
    - add 12 scores, including a tie;
    - `Entries.Count == 10`, in order;
    - the tied new entry ranks below the old one;
    - `Qualifies(0)` is false;
    - with a full table, `Qualifies(lowest)` is false and `Qualifies(lowest + 1)` is true.
  - `Unreadable_high_scores_are_kept_as_bad_and_a_new_table_starts`:
    - write `{ not json` to `highscores.json`;
    - loading gives an empty table and the exact notice;
    - `highscores.bad.json` holds the original bytes;
    - a later save writes a valid file.
  - `Out_of_range_settings_fall_back_per_field`: with `"textSize": "Huge"`, `"throttleScheme": 7` and `"screenShake": false`, the text size is Normal, the scheme is 0, shake is false (kept), and there is no notice.
  - `Bad_high_score_entries_are_skipped`: initials `"ab1"`, a negative score and `missionReached` 9 are all skipped. Lower-case `"abc"` is upper-cased.
  - `A_failed_save_reports_a_problem_and_does_not_throw`: make the store's directory path an existing **file**. `SaveSettings` returns `Ok == false` with a `COULD NOT SAVE SETTINGS:` notice.
  - `Saving_writes_through_a_temp_file`: after a save, no `.tmp` file remains and the JSON parses.
- [ ] **Step 2: Run** `dotnet test tests/NightPursuit.Core.Tests --filter GameStoreTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
  - Use `JsonDocument` for reading, so each field falls back on its own, and `Utf8JsonWriter` or `JsonSerializer` for writing.
  - Write `name.tmp`, then `File.Move(tmp, path, overwrite: true)`.
  - Catch `IOException`, `UnauthorizedAccessException`, `JsonException`, `NotSupportedException` and `ArgumentException` (the loader's own list, MissionLoader.cs:17). A read-only folder throws `UnauthorizedAccessException`.
  - A valid `missionReached` is 1 to `Campaign.MissionIds.Length`.
  - `MemoryGameStore` has `bool FailSaves` (its saves then return `Ok == false` with a `COULD NOT SAVE …` problem), plus `int SettingsSaves` and `int HighScoreSaves`.
- [ ] **Step 4: Run** the whole test project. Expected: PASS.
- [ ] **Step 5: Commit** `feat: settings and high scores saved as JSON`.

### Task 5: Menus, initials entry and the screen flow

**Files:**
- Create: `src/NightPursuit.Core/Session/MenuKey.cs`, `Menu.cs`, `InitialsEntry.cs`, `ScreenFlow.cs`
- Test: `tests/NightPursuit.Core.Tests/ScreenFlowTests.cs`

**Interfaces:**
- Consumes: `Game` (Task 3), `GameSettings`, `HighScoreTable` and `IGameStore` (Task 4).
- Produces:
  - **Keys:**
    - `public enum MenuInput { Up, Down, Left, Right, Confirm, Back, Delete, Letter, Pause, Help }`.
    - `public readonly record struct MenuKey(MenuInput Input, char Letter = '\0')`.
  - **Menus** (named so they don't clash with WPF's `Menu` and `MenuItem` in App code):
    - `public sealed record MenuEntry(string Id, string Label, string? Value = null)`.
    - `public sealed class MenuModel` with `string Title`, `IReadOnlyList<string> Lines`, `IReadOnlyList<MenuEntry> Items`, `int Selected`, `MenuEntry Current`, and `void Move(int delta)` (wraps).
  - **`public sealed class InitialsEntry`:**
    - `InitialsEntry(string start)` keeps up to 3 letters A–Z, upper-cased, and pads with `A`;
    - `string Text`, `int Cursor` (0–2);
    - `Up()` A→B, …, Z→A, and `Down()` the reverse;
    - `Left()` and `Right()` clamp at the ends;
    - `Type(char c)` takes A–Z in any case (it sets the letter and moves right, staying at 2); anything else is ignored;
    - `Delete()` moves the cursor left.
  - **Screens:** `public enum Screen { Title, Briefing, Play, Pause, Settings, Controls, QuitConfirm, MissionComplete, GameOver, Finished, EnterInitials, HighScores }`.
  - **`public sealed class ScreenFlow`:**
    - `ScreenFlow(Func<bool, Game> newGame, FeelSettings feel, GameSettings settings, HighScoreTable scores, IGameStore store)`, where `newGame(practice)`;
    - `Screen Current`, `Game? Game`, `MenuModel? Menu` (for Title, Pause, Settings and QuitConfirm), `InitialsEntry? Initials`;
    - `int NewRank` (0 when none);
    - `string? Notice`: several notices join with ` / `, and it clears on the next screen change after it has been shown on one screen;
    - `int Version`: it goes up on every visible change;
    - `bool ExitRequested`, `bool ShowIntroHelp`, `void IntroHelpSeen()` (GameLoop's gas rule calls it);
    - `GameSettings Settings`, `HighScoreTable Scores`, `FeelSettings Feel`;
    - "(return)" in the table below uses **a return stack**: Pause → Settings → KEY BINDINGS → Controls, then Back, Back, reaches Settings and then Pause;
    - `void StartGame(bool practice = false)`: a new Game, `Current = Briefing`;
    - `void Handle(MenuKey key)`: used on every screen except Play;
    - `void HandleGameAction(GameAction a)`: Play only;
    - `void Update(ControlFrame held, double seconds)`: steps the game only in Play, then syncs the screen;
    - `void FocusLost()`;
    - `void ShowNotice(string? notice)`.
  - `Settings` applies at once:
    - `feel.SpeedSensitiveSteering = settings.LighterSteering`;
    - a scheme change while a run exists calls `Run.World.Handle(new GameAction(ActionKind.SelectScheme, i))` directly, because `MissionRun.Handle` passes it on only while Playing, and from Pause the run is Paused;
    - the App reads `Settings.ScreenShake` and `Settings.TextScale` each frame.

**What each screen does** (exact menu copy). In this table, "→ X" means go to X, and "(return)" means go back to the screen that opened this one.

| Screen | Shows | Keys |
|---|---|---|
| Title | Menu: title `NIGHT PURSUIT`, line `SIX MISSIONS. THREE SPARE CARS. ONE LONG NIGHT.`; items `START GAME` (start), `HIGH SCORES` (scores), `SETTINGS` (settings), `CONTROLS` (controls), `QUIT` (quit) | Up/Down move; Confirm acts; quit sets `ExitRequested`; Back and Pause do nothing; Help → Controls |
| Briefing | (the view reads `Game.Mission`) | Confirm → `Game.StartMission()`, Play, `ShowIntroHelp = MissionIndex == 0`; Back → QuitConfirm; Help → Controls |
| Play | the game | `HandleGameAction` (below) |
| Pause | Menu: title `PAUSED`, line `MISSION {n} OF {count}: {TITLE}` (practice: `PRACTICE: {TITLE}`); items `RESUME`, `RESTART MISSION` (practice only), `SETTINGS`, `CONTROLS`, `QUIT TO TITLE` | Back, Pause and Letter P resume (one unpause: `Run.Handle(Pause)`, then Play); restart, or Letter R in practice → `Game.RestartMission()`, Play (R does nothing in a campaign); Help → Controls |
| Settings | Menu: title `SETTINGS`; items `CONTROLS` value `RAMP PEDAL + CRUISE` or `SPEED LEVER`, `LIGHTER STEERING AT SPEED` `ON`/`OFF`, `SCREEN SHAKE` `ON`/`OFF`, `TEXT SIZE` `NORMAL`/`LARGE`/`LARGEST`, `KEY BINDINGS`, `BACK` | Left/Right/Confirm change the selected value (wrapping); `KEY BINDINGS` → Controls; Back or `BACK` saves (a failed save sets `Notice`) and returns |
| Controls | (the view shows the help lines) | Confirm, Back, Help and Pause return; if it was opened from Play, it resumes the run |
| QuitConfirm | Menu: title `QUIT THIS GAME?`, line `YOUR SCORE STILL COUNTS FOR THE HIGH SCORES.`; items `NO, KEEP PLAYING` (selected first), `YES, QUIT` | yes → `Game.Quit()`, GameOver; no or Back → return |
| MissionComplete | (the view reads `Game.LastBonus`) | Confirm → in practice, Title (the game is dropped); else `Game.Continue()`, Briefing or Finished |
| GameOver, Finished | (the view reads `Game`) | Confirm → in practice, Title; else if `Scores.Qualifies(Game.Score)`, EnterInitials (`new InitialsEntry(Settings.LastInitials)`); else HighScores with `NewRank = 0` |
| EnterInitials | (the view reads `Initials` and `Scores.RankFor`) | a key whose `Letter` isn't `'\0'` (so W, A, S and D too) types that letter; otherwise Up/Down/Left/Right/Delete edit; Confirm adds the entry (`MissionReached`, `FinishedAll`), sets `LastInitials`, saves both files, then HighScores with `NewRank`; Back does nothing |
| HighScores | (the view reads `Scores`, `NewRank`) | Confirm or Back → Title (the game is dropped) |

The game is stepped only while `Current == Play`, so the road is frozen behind every screen.

**In Play**, `HandleGameAction`:
- `ToggleHelp` opens Controls, to return to Play. If the run is Playing it pauses it, and on return it unpauses only a run it paused itself (during a respawn it stays as it was);
- `SelectScheme` also sets `Settings.SchemeIndex` and saves;
- everything else goes to `Game.Handle`;
- once an action has moved the flow off Play (for example `ToggleHelp`), the rest of that frame's game actions are ignored.

In the run itself, R while driving publishes "PRESS P TO PAUSE, THEN R TO RESTART" (MissionRun.cs:91). That stays true in practice, and in a campaign `Game.Handle` drops R before the run sees it.

**`Update` syncs the screen** after stepping the game, and only when `Current == Play`:
- `Run.State == Paused` → Pause;
- `Game.Stage == MissionComplete` → MissionComplete;
- `GameOver` → GameOver;
- `Finished` → Finished.

**`FocusLost`** in Play pauses the run if it is Playing, so the next `Update` shows Pause. Anywhere else it does nothing. It is safe to call every frame: GameLoop calls it on each frame the window is inactive, so a respawn that ends while unfocused is paused at once (D12; `Run.Handle(Pause)` is ignored while Respawning).

- [ ] **Step 1: Write the failing tests.** Use two in-code missions and `MemoryGameStore`.
  - `InitialsEntry_edits` checks:
    - `"jm"` gives `JMA`;
    - Up on Z gives A;
    - typing `x` at cursor 2 keeps the cursor at 2;
    - `Delete` at 0 stays at 0;
    - typing `1` is ignored.
  - `Menu_moves_and_wraps`.
  - `Start_game_goes_to_briefing_then_play_with_intro_help_only_first`.
  - `Esc_in_play_pauses_and_Esc_in_pause_resumes_once`: `HandleGameAction(Pause)` then `Update` gives Pause; `Handle(Back)` gives Play and `Run.State == Playing`.
  - `F1_in_play_pauses_and_returns`: Controls is shown with the run Paused; Back gives Play with the run Playing.
  - `Quit_needs_confirmation`:
    - Back on a Briefing → QuitConfirm, with `NO, KEEP PLAYING` selected;
    - Confirm → Briefing again;
    - Back, Down, Confirm → GameOver with `Game.QuitEarly`.
  - `Quitting_with_a_qualifying_score_still_asks_for_initials`: score 500 into an empty table; pause, quit, confirm YES → GameOver; Confirm → EnterInitials.
  - `High_score_entry_saves_and_highlights`:
    - type `J`, `M`, `A`, then Confirm;
    - the table has `JMA` at rank 1;
    - `Settings.LastInitials == "JMA"`;
    - the store saved both files;
    - HighScores with `NewRank == 1`.
  - `Practice_games_never_record`:
    - a practice MissionComplete, then Confirm → Title;
    - a practice GameOver, then Confirm → Title;
    - the table is unchanged.
  - `R_restarts_only_in_practice`: on the Pause screen, `Letter('R')` gives a new World and Play in practice, and does nothing in a campaign.
  - `Settings_change_and_save_on_back`:
    - Right on `TEXT SIZE` gives Large, and `TextScale == 1.25`;
    - turning lighter steering off sets `feel.SpeedSensitiveSteering` false;
    - opened from Pause, changing the scheme gives `Run.World.SchemeIndex == 1`;
    - Back saves once.
  - `Return_stack_unwinds`: Pause → Settings → KEY BINDINGS → Controls; Back → Settings; Back → Pause.
  - `A_failed_save_shows_a_notice`: a store whose saves fail sets `Notice`.
  - `Every_screen_leads_back_to_title_or_play`: from each reachable screen, pressing Confirm (or Back where Confirm stays) at most 6 times reaches Title or Play. Walk Title, Briefing, Pause, Settings, Controls, QuitConfirm, MissionComplete, GameOver, Finished, EnterInitials and HighScores.
  - `Focus_lost_only_matters_in_play`: also, FocusLost during Respawning, then FocusLost and Update each frame until the respawn ends, gives Pause.
- [ ] **Step 2: Run** `dotnet test tests/NightPursuit.Core.Tests --filter ScreenFlowTests`. Expected: FAIL.
- [ ] **Step 3: Implement** to the table.
- [ ] **Step 4: Run** the whole test project. Expected: PASS.
- [ ] **Step 5: Commit** `feat: screen flow, menus and initials entry`.

### Task 6: Missions 2 to 6

**Files:**
- Create: `content/missions/mission-02.json` to `mission-06.json`
- Test: `tests/NightPursuit.Core.Tests/CampaignTests.cs`

**Interfaces:**
- Consumes: `Campaign.MissionIds` (Task 3) and the `bonus` and `look` keys (Task 1).

**The six missions.** Mission 1 is the existing file, unchanged.

| # | id / title | road | approach | traffic | police | loadout oil/smoke/cal/mis/strike | bonus | boss (health, look) | phases |
|---|---|---|---|---|---|---|---|---|---|
| 1 | mission-01 Midnight Run | seed 7 | 6000 | 3 | presence 1 | 5/1/1/1/0 | (1000) | Black Heron (180, Fin) | Blocker <0.6, Pursuer <0.25, Runner |
| 2 | mission-02 Harbour Lights | seed 12, minRadius 220, maxRadius 800, hillAmplitude 8 | 6500 | 4 | presence 1 | 5/2/1/1/0 | 1500 | Iron Gull (220, Twin) | Blocker <0.55, Gunner |
| 3 | mission-03 Glass Canyon | seed 19, minRadius 140, maxRadius 500, hillAmplitude 26 | 7000 | 2 | presence 0.5 | 5/2/2/1/0 | 2000 | Red Lantern (260, Dome) | Dropper <0.65, Pursuer <0.3, Runner |
| 4 | mission-04 Neon Mile | seed 27, minRadius 260, maxRadius 900, hillAmplitude 10 | 7500 | 7 | presence 1.5, startingHeat 30 | 5/2/2/2/1 | 2500 | Grey Warden (300, Twin) | Blocker <0.6, Armored <0.3, Gunner |
| 5 | mission-05 Long Causeway | seed 41, minRadius 400, maxRadius 1200, hillAmplitude 6 | 8000 | 4 | presence 1.5 | 5/2/2/2/1 | 3000 | Silver Kite (340, Fin) | Runner <0.6, Pursuer <0.25 **missiles**, Gunner |
| 6 | mission-06 Last Light | seed 53, minRadius 160, maxRadius 650, hillAmplitude 22 | 9000 | 5 | presence 2, startingHeat 25 | 5/3/2/2/1 | 4000 | Obsidian Crown (400, Dome) | Gunner <0.66, Dropper <0.33, Pursuer **missiles** |

**Waves**, as `kind×count@atDistance`. Use spawnOffset −250 for every Pursuer wave, +600 for a count of 3 or more, and +500 otherwise.
- **2:** Runner×2@400, Blocker×2@1300, Gunner×1@2200, Pursuer×1@3000, Runner×3@3800, Gunner×2@4700, Blocker×2@5500
- **3:** Runner×2@400, Dropper×1@1200, Blocker×2@2000, Pursuer×2@2900, Dropper×2@3800, Gunner×1@4700, Runner×3@5500, Dropper×2@6300
- **4:** Blocker×2@400, Armored×1@1300, Runner×3@2100, Gunner×2@2900, Armored×1@3700, Pursuer×2@4500, Dropper×2@5300, Armored×2@6300
- **5:** Runner×3@400, Gunner×2@1200, Pursuer×2@2000, Armored×1@2800, Dropper×2@3600, Blocker×3@4400, Gunner×3@5200, Pursuer×2@6000, Armored×2@7000
- **6:** Blocker×3@400, Gunner×2@1200, Dropper×2@2000, Armored×2@2800, Pursuer×3@3600, Runner×3@4400, Gunner×3@5200, Armored×2@6000, Dropper×3@6800, Pursuer×3@7600, Armored×2@8300

**Crates**, as `item×amount@atDistance lane`:
- **2:** Missile×1@1500 L1, Smoke×1@2800 L−1, Oil×2@4200 L0, Armor×1@5800 L1
- **3:** Caltrops×1@1600 L0, Missile×1@3200 L1, Smoke×1@4500 L−1, Armor×1@6000 L0
- **4:** Missile×1@1000 L1, Missile×1@2500 L−1, Smoke×1@3500 L0, Strike×1@5000 L1, Armor×1@6800 L−1
- **5:** Missile×1@1500 L0, Smoke×1@3000 L1, Armor×1@4000 L−1, Strike×1@5500 L0, Armor×1@7300 L1
- **6:** Missile×2@1500 L1, Smoke×1@3000 L−1, Armor×1@4200 L0, Strike×1@5600 L1, Missile×1@7000 L−1, Armor×1@8000 L0

**Briefings** (exact text):
- **2:** "The syndicate moves its cargo through the old harbour after midnight. Their gunners ride along now and shoot back through the rear glass. Break the convoy's guard and bring down the Iron Gull before it reaches the docks."
- **3:** "The canyon road climbs through tight bends and blind crests. The syndicate's droppers will leave oil and spikes on every corner. Keep your line, keep your nerve, and run down the Red Lantern at the summit."
- **4:** "The city expressway never sleeps, and tonight the police are already watching you. Armored cars shrug off your guns, so save a missile or steer them into a wreck. The Grey Warden waits at the end of the mile."
- **5:** "A straight, fast road over black water, with nowhere to hide. The Silver Kite carries homing missiles: watch for the warning, then change lanes in the last second or shoot them down. Don't let it reach the far shore."
- **6:** "The syndicate's chief drives the mountain road home before dawn. Every car they have left is out tonight, and the police are close behind. This is the last run. Bring down the Obsidian Crown."

- [ ] **Step 1: Write the failing tests** in `CampaignTests.cs`:
  - `All_six_campaign_missions_load`: every `Campaign.MissionIds` file loads.
  - `Campaign_rises_in_difficulty`: boss health strictly rises; approach length never falls; road seeds are all different.
  - `Only_missions_5_and_6_have_missile_phases`: exactly one `FiresMissiles` phase each in 5 and 6, and none in 1–4 (ENM-4).
  - `Mission_values_match_the_plan`: spot-check 2 and 6 against the table (title, approach, traffic, police, bonus, boss name, health, look and phase count).
  - `Every_campaign_mission_runs_90_seconds_without_error`:
    - for each mission, a `MissionRun` (seed 1) for 90 s of `Update(…, 1/60.0)`;
    - drive with `new ControlFrame { Gas = true, PadConnected = true, PadSteer = EnemyMissileTests.AutopilotSteer(run.World) }`, as the existing long tests do. Steering 0 drifts into the rail on tight roads;
    - assert no exception, and `Player.Distance > 1000` or `State is GameOver or Completed`.
- [ ] **Step 2: Run** `dotnet test tests/NightPursuit.Core.Tests --filter CampaignTests`. Expected: FAIL (files missing).
- [ ] **Step 3: Write the five JSON files** to the table, in mission-01.json's shape. The JSON keys are:
  - top-level `"bonus"`;
  - boss `"look"`;
  - phase `"missiles": true`;
  - `"police": { "presence", "startingHeat" }`;
  - `"traffic": { "carsPerKm" }`;
  - `"loadout": { "oil", "smoke", "caltrops", "missiles", "strikes" }`;
  - `"road": { "seed", "minRadius", "maxRadius", "hillAmplitude" }`.

  The loader's own validation is the check on wave windows.
- [ ] **Step 4: Run** the whole test project. Expected: PASS.
- [ ] **Step 5: Commit** `feat: missions 2 to 6`.

### Task 7: Screen host, Title, Pause, Settings and Quit screens, and menu input

**Files:**
- Create:
  - `src/NightPursuit.App/AppPaths.cs`: `public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NightPursuit");`
  - `Screens/NeonStyles.xaml`, `Screens/ScreenHost.cs`, `Screens/MenuScreen.xaml(.cs)`
  - `Diagnostics/ScreenSnapshots.cs`
- Modify:
  - `App.xaml` (merge `NeonStyles.xaml`);
  - `App.xaml.cs`;
  - `MainWindow.xaml(.cs)`;
  - `GameLoop.cs`;
  - `Input/InputHub.cs` and `Input/XInputPad.cs`;
  - `Rendering/GameView.cs`;
  - `Diagnostics/DiagnosticsRunner.cs` (only to dispatch `--screen`).

**Interfaces:**
- Consumes: `ScreenFlow`, `Game`, `FileGameStore` and `MemoryGameStore` (Tasks 3–5), and `Campaign.MissionIds`.
- Produces:
  - **Menu keys from `InputHub`:**
    - `IEnumerable<MenuKey> DrainMenu()`. `OnKeyDown` also queues **one** menu key per press, with key repeat allowed for the four directions only:
      - the arrow keys → Up, Down, Left, Right;
      - W, A, S, D → the same directions, carrying their letter: `new MenuKey(MenuInput.Up, 'W')` and so on;
      - every other A–Z key, including P → `new MenuKey(MenuInput.Letter, ch)`;
      - Enter and Space → Confirm;
      - Esc → Back;
      - Backspace → Delete;
      - F1 → Help.
    - The pad, on a press edge: D-pad Up/Down/Left/Right → the directions, A → Confirm, B → Back, Start → Pause, Back → Back.
    - The D-pad Left/Right constants must be added (`0x0004`, `0x0008`).
  - **`ScreenHost : ContentControl`** with `void Show(ScreenFlow flow)`. It swaps or refreshes the view when `flow.Current` or `flow.Version` changes, and sets the text-size resources.
    - The views are built in a 1280×720 design `Grid`, inside an outer `Viewbox` (Uniform), with an inner `Viewbox` (`StretchDirection="DownOnly"`) around the content, so too much text shrinks instead of being cut off.
    - The backdrop is solid black on Title, Briefing and HighScores, and `#E6040810` over the frozen road elsewhere.
  - **`NeonStyles.xaml`:**
    - Consolas;
    - the brushes `Neon.TextAmber/Cyan/White/Gray/Green/Red` as resources;
    - the font sizes `TextSmall` 16, `TextBody` 20, `TextHeading` 30 and `TextTitle` 56, as `DynamicResource` doubles that `ScreenHost` multiplies by `Settings.TextScale`.
  - **`MenuScreen`** draws `Menu.Title`, `Menu.Lines`, the items (selected: `> LABEL   < VALUE >` in cyan; others: `  LABEL   VALUE` in white) and `flow.Notice` in amber.
  - **`GameView`** gains `bool ShakeEnabled = true` and `double TextScale = 1`, read each frame from `flow.Settings`. `TextScale` is wired in Task 10; this task only adds the property.
  - **`MainWindow(ScreenFlow flow)`:**
    - `Root` holds `GameView` and then `ScreenHost`.
    - Rename the `GameView` element from `Game` to `View`, since `Game` is now a Core type.
    - `Deactivated` → `_input.ReleaseAll(); flow.FocusLost();`.
    - The DEBUG tuning panel uses `flow.Feel`.
  - **`ScreenHost` placeholders:** until Tasks 8 and 9 add their views, Briefing, Controls, the result screens, EnterInitials and HighScores show a plain placeholder `TextBlock` with the screen's name. Play shows no view (`Content = null`).
  - **Code-behind** that also uses WPF menus refers to the Core types as `MenuModel` and `MenuEntry`.
  - **`GameLoop(Window window, ScreenFlow flow, GameView view, ScreenHost host, InputHub input)`.** Each frame, after the existing `_everActive` gate:
    1. If `!_window.IsActive`, call `flow.FocusLost()` (every frame, as the old per-frame pause did, D12).
    2. Read the held controls.
    3. If `flow.Current == Screen.Play` at the start of the frame:
       - drain the game actions to `flow.HandleGameAction`;
       - **discard** the menu keys;
       - run the gas-to-start rule: gas while `flow.ShowIntroHelp` calls `flow.IntroHelpSeen()`. This replaces `_gasSeen`.
    4. Otherwise:
       - drain the menu keys to `flow.Handle`;
       - **discard** the game actions.
    5. **Fire latch:** when the frame started on a screen and ends in Play, set `_fireLatch`. While it is set, pass `Fire = false` and `PadFire = false` in the held frame. Clear it once both are released. Space and pad A both confirm *and* fire, so without this, confirming on the briefing or on RESUME would start the guns.
    6. Call `flow.Update(held, seconds)`.
    7. Set `view.Run = flow.Game?.Run`, `view.ShowHelp = flow.ShowIntroHelp && flow.Current == Screen.Play`, and `view.ShakeEnabled = flow.Settings.ScreenShake`. Absorb the effects as now. Call `host.Show(flow)`.
    8. If `flow.ExitRequested`, close the window.
  - **`App.OnStartup`:**
    - The diagnostics gate is unchanged.
    - `store = new FileGameStore(AppPaths.DataDirectory)`; load the settings and high scores. A load problem goes to `flow.ShowNotice`, shown on the Title.
    - `feel = new FeelSettings { SpeedSensitiveSteering = settings.LighterSteering }`.
    - Missions:
      - always load all of `Campaign.MissionIds`. `newGame(false)` uses them, so START GAME is always the real campaign;
      - with `--mission name`, also load that file (validated as now). `newGame(true)` uses it, and the flow starts with `flow.StartGame(practice: true)`;
      - otherwise start on the Title.
    - Several load notices join into one (see `Notice`).
    - A missing or bad mission file still uses `Fail(...)` as now.
  - **Diagnostics:** `--snapshot out.png --screen name[,name] [--mission m] [--size WxH] [--text-size normal|large|largest]`.
    - It builds the flow with `MemoryGameStore` and a sample table of 7 entries.
    - It drives the flow through its public API to each screen.
    - It renders a `Grid(GameView + ScreenHost)` at the size to the PNG (Measure, Arrange, `UpdateLayout`, then `RenderTargetBitmap`), with the same out-name rule as `--scene`.
    - The screen names are `title, settings, pause, quit` in this task; later tasks add theirs.
    - Unknown names → exit code 2 with the list.
    - The new code goes in `ScreenSnapshots.cs`.

The test project references Core only, so this task has no unit tests of its own. Its rules are tested in Task 5, and its checks are the build and the snapshots.

- [ ] **Step 1: Implement** the files above.
- [ ] **Step 2: Build** with `dotnet build src/NightPursuit.App -c Release`. Expected: 0 errors, 0 warnings.
- [ ] **Step 3: Run** the tests. Expected: PASS (nothing in Core changed).
- [ ] **Step 4: Snapshot** in one launch: `NightPursuit.exe --snapshot snapshots/t7.png --screen title,settings,pause,quit`. Expected: exit 0 and four PNGs. Read each PNG:
  - the Title menu has `> START GAME` marked;
  - Pause shows over the frozen road;
  - Settings shows all six rows with their values.
- [ ] **Step 5: Commit** `feat: screen host with title, pause, settings and quit screens`.

### Task 8: The briefing screen and the boss looks

**Files:**
- Create: `src/NightPursuit.App/Screens/BriefingScreen.xaml(.cs)`, `Screens/BossPortrait.cs`
- Modify: `src/NightPursuit.Core/Vehicles/BossCar.cs`, `Rendering/VehicleRenderer.cs` (boss shapes by look), `Screens/ScreenHost.cs`, `Diagnostics/ScreenSnapshots.cs`
- Test: `tests/NightPursuit.Core.Tests/BossTests.cs`

**Interfaces:**
- Consumes: `BossLook` (Task 1), `Game.Mission`, `MissionIndex`, `Score`, `SpareCars` and `Practice`, and `HeatRules.LevelOf`/`LevelName`.
- Produces:
  - **Boss shapes** (drawn with the `LineArt` pens: an outline, gaps, no line along the base, as the line-art restyle did for the boss in play): `internal static class BossShapes` (in `VehicleRenderer.cs` or its own file) with `Body(BossLook)` and `Fins(BossLook)`, returning the point tables. `Fin` = today's `BossShape` and `BossFin`. `Twin` = the body widened to 1.1 with two fin pairs at x 0.92 and 0.55. `Dome` = no fins, with a raised cabin outline from y 0.8 to 1.35 between x ±0.5.
  - In play, the boss uses its definition's look. `BossCar` already holds its `Definition` (BossCar.cs:20), so add `public BossLook Look => Definition.Look;`. The director needs no change.
  - **`BossPortrait : FrameworkElement`** with `BossLook Look` and `string Name`. It draws the rear view about 300×180 in the boss colour with the `LineArt` pens (outline, gaps, no base line; the same pens as the boss in play), through the same shape tables.
  - **`BriefingScreen`** copy, top to bottom:
    - `MISSION {n} OF {count}` (practice: `PRACTICE`);
    - `{TITLE}` (heading);
    - the briefing text (body, wrapped at 900 px);
    - `BOSS: {NAME}` and the portrait;
    - `{k} PHASES`, plus `  -  FIRES MISSILES: WATCH FOR THE MISSILE WARNING` when any phase fires missiles;
    - `LOADOUT   OIL {o}   SMOKE {s}   CALTROPS {c}   MISSILES {m}   STRIKES {x}`;
    - `ROAD      {approach/1000:0.0} KM TO THE BOSS`;
    - `TRAFFIC   {NONE|LIGHT|MODERATE|HEAVY}` + ` ({n} CARS PER KM)` when above 0;
    - `POLICE    {NONE|LOW|NORMAL|HIGH}` + `   STARTING HEAT {h}: {LEVEL NAME}` when the starting heat is above 0;
    - `SCORE {score:00000}   SPARE CARS {n}`;
    - the bottom line `ENTER  DRIVE      ESC  QUIT      F1  CONTROLS`.
    - The words follow Call 13. A mission with no boss shows `NO BOSS: REACH THE END OF THE ROAD` instead of the boss lines.

- [ ] **Step 1: Implement.** There are no Core changes except passing the look to `BossCar`. Add `BossCar_takes_its_look_from_the_definition` to `BossTests` (Core), and run it to see it fail first.
- [ ] **Step 2: Build and run the tests.** Expected: 0 warnings, PASS.
- [ ] **Step 3: Snapshot in one launch:** `--snapshot snapshots/t8.png --screen briefing-all`. The new screen name `briefing-all` writes one PNG per campaign mission (`t8-briefing-1.png` … `t8-briefing-6.png`). Plain `briefing` uses `--mission`, as the other screens do. Read them:
  - each boss has its own look and name;
  - mission 5 shows `FIRES MISSILES`;
  - mission 4 shows `STARTING HEAT 30: WATCHED`.
- [ ] **Step 4: Commit** `feat: briefing screen with boss portraits and three boss looks`.

### Task 9: Controls, results, initials and high-score screens; F1 pauses

**Files:**
- Create: `Screens/ControlsScreen.xaml(.cs)`, `Screens/ResultScreen.xaml(.cs)`, `Screens/InitialsScreen.xaml(.cs)`, `Screens/HighScoresScreen.xaml(.cs)`
- Modify: `Rendering/Overlays.cs`, `Rendering/Hud.cs`, `Screens/ScreenHost.cs`, `Diagnostics/ScreenSnapshots.cs`

**Interfaces:**
- Consumes: `ScreenFlow` (`Initials`, `NewRank`, `Scores`, `Notice`) and `Game` (`LastBonus`, `QuitEarly`, `MissionReached`, `FinishedAll`, `Practice`).
- Produces:
  - **Shared help text:** `Overlays.HelpLines` becomes `internal static IReadOnlyList<string> HelpLines`, shared with `ControlsScreen`. `DrawHelp`'s uses of `.Length` become `.Count`.
    - The GAME line becomes `GAME       P / Esc pause   F1 controls (pauses)   R restart (practice only)`.
    - The gamepad lines say `Back restart (practice)` and add `MENUS      A select   B back   Start resume` (spec §8.3 puts the pad mapping here).
    - The intro box's last line stays `Press gas to start.  F1 shows this again.`. `ControlsScreen` leaves that line out.
  - **The HUD's top-left line** becomes `F1 controls  P pause`.
  - **`Overlays.DrawState`** now draws only the Respawning box. Paused, Completed and GameOver are screens now.
  - **`ControlsScreen`:** the help lines, then `ENTER OR ESC  BACK`.
  - **`ResultScreen`** for MissionComplete:
    - `MISSION {n} COMPLETE` (practice: `PRACTICE COMPLETE`);
    - `{BOSS} DESTROYED` if there is a boss;
    - `MISSION BONUS          +{base}`;
    - `ARMOR LEFT {a} x 10    +{a*10}`;
    - `NO CAR LOST            +1000` or `A CAR WAS LOST         +0`;
    - `SCORE {score:00000}   SPARE CARS {n}`;
    - `ENTER  NEXT MISSION` (after the last mission: `ENTER  CONTINUE`).
  - **`ResultScreen`** for GameOver:
    - `GAME OVER` (`GAME ENDED` when quit early);
    - `MISSION REACHED {n} OF {count}`;
    - `FINAL SCORE {score:00000}`;
    - `ENTER  CONTINUE`.
  - **`ResultScreen`** for Finished:
    - `ALL SIX MISSIONS COMPLETE`;
    - `THE SYNDICATE IS BROKEN. THE ROAD IS YOURS.`;
    - `FINAL SCORE {score:00000}`;
    - `ENTER  CONTINUE`.
  - **`InitialsScreen`:**
    - `NEW HIGH SCORE  -  RANK {r}`;
    - `{score:00000}`;
    - the three letters large, with the cursor letter shown as `[J]`, underlined and in cyan, and the others as ` M `;
    - `TYPE LETTERS, OR UP/DOWN TO CHANGE AND LEFT/RIGHT TO MOVE.  ENTER SAVES.`
  - **`HighScoresScreen`:**
    - `HIGH SCORES`;
    - rows `{rank,2}. {initials}   {score:00000}   MISSION {n}`, or `ALL 6` when finished;
    - the new row ends with `  NEW` and is cyan;
    - `NO SCORES YET` when the table is empty;
    - the notice line;
    - `ENTER  BACK TO TITLE`.
  - **Screen names for `--screen`:** `controls, complete, gameover, ended, finished, initials, highscores`.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build and run the tests.** Expected: 0 warnings, PASS.
- [ ] **Step 3: Snapshot in one launch:** `--screen controls,complete,gameover,ended,finished,initials,highscores`. Read each PNG against the copy above, including the `[J]` cursor and the `NEW` marker.
- [ ] **Step 4: Commit** `feat: controls, result, initials and high-score screens; F1 pauses`.

### Task 10: Text size in the HUD, and screen shake off

**Files:**
- Modify:
  - `Rendering/HudLayout.cs`, `Rendering/GameView.cs`;
  - `Rendering/Overlays.cs`, `Rendering/Hud.cs`, `Rendering/WeaponPanel.cs`, `Rendering/HeatMeterView.cs`, `Rendering/BossBar.cs`, `Rendering/Mirror.cs`, `Rendering/Radar.cs` (wherever text or a text-holding box is sized);
  - `Diagnostics/ScreenSnapshots.cs` and `DiagnosticsRunner.cs` (`--text-size` for `--scene` too).

**Interfaces:**
- Produces:
  - `HudLayout.TextScale` (`double`, default 1). `Text` and `TextWidth` use `Math.Max(8, size * K * TextScale)`.
  - `double T(double v) => v * K * TextScale`, for every row height, line gap and box height that holds text.
  - `GameView` sets `_layout.TextScale = TextScale` in `Draw`. The shake transform is skipped when `!ShakeEnabled`.
  - Boxes that hold text grow with it: help, messages, warnings, announcement, busted, respawning, the weapon panel caption, the heat meter label and the readout block.
  - `Hud.ReadoutBounds` and `WeaponPanel.Bounds` use `T` for their text parts, so the other HUD parts still avoid them.
  - `--size` and `--text-size` accept comma lists (`--size 1280x720,960x540 --text-size normal,largest`). With more than one value, each file name gets a `-WxH` or `-normal` suffix, so one launch covers them all.

- [ ] **Step 1: Implement.** At `TextScale` 1, every position and size must come out exactly as before. A `--scene default` snapshot at Normal must match the Plan 3 drawing by eye.
- [ ] **Step 2: Build.** Expected: 0 warnings.
- [ ] **Step 3: Snapshot in one launch:** `--snapshot snapshots/t10.png --scene default,help,busted,boss --text-size normal,largest --size 1280x720,960x540`. Read the PNGs. The Normal ones match Plan 3's drawing, and in the Largest ones:
  - no text crosses its panel's edge;
  - the help box holds all its lines;
  - the readouts don't overlap the heat meter.
- [ ] **Step 4: Commit** `feat: text size setting in the HUD; screen shake can be turned off`.

### Task 11: Stress, perf and the full check

**Files:**
- Modify: `src/NightPursuit.App/Diagnostics/DiagnosticsRunner.cs` (stress lists and cadence), `Diagnostics/ScreenSnapshots.cs` (a stress entry point)

**Interfaces:**
- Produces:
  - `StressMissions` = the six campaign missions, `proving-ground` and `patrol-ground`.
  - The cadence per size: `(960,540)` every 10 m; `(320,200)` and `(2560,600)` every 100 m. Plus the minimised frame per mission.
  - The HUD's `TextScale` cycles 1, 1.25, 1.5 by frame number.
  - A screens pass: every `--screen` name × the three stress sizes × the three text sizes.
  - The report states the frame counts per part and the minutes taken.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build and run the tests.** Expected: 0 warnings, PASS. Record the count.
- [ ] **Step 3: Run the stress test in the background:** `NightPursuit.exe --stress snapshots/stress.txt`. Expected: exit 0, 0 failures. Record the frames and the minutes.
- [ ] **Step 4: Run** `NightPursuit.exe --perf snapshots/perf.txt`. Expected: mean under 8 ms. Record the mean, p95 and max.
- [ ] **Step 5: Commit** `test: stress covers six missions, every screen and text size`.

### Task 12: Decisions log and the owner's checks

**Files:**
- Modify: `docs/decisions.md`

- [ ] **Step 1: Add D131 onward,** one per "Calls made" item, plus any rulings made during the build. Each one is in the log's existing style: what was decided, why, and *Cost to change*.
- [ ] **Step 2: Update "Open items":**
  - close "F1 help doesn't pause the game" and "You respawn where you crashed";
  - replace "Coming in later plans" with "After version 1 (design §10): police helicopters and enemy spies. Sound (design §9)";
  - add any new open items found in the build.
- [ ] **Step 3: Add "Checks only you can do (Plan 4)":**
  1. Play the campaign from the title: `dotnet run -c Release --project src/NightPursuit.App`. Judge each mission's difficulty and length, the rise from 1 to 6, and the bonus numbers.
  2. Missions 5 and 6: the missile phases (D44 warnings) feel fair.
  3. Get a high score, enter initials, and check `%AppData%\NightPursuit\highscores.json` is there after you close and reopen the game.
  4. Settings: try each text size, shake off, lighter steering off, and the speed lever. Check they're kept after a restart.
  5. Press F1 in play: the game pauses and comes back.
  6. Quit from the pause menu: it asks first, and your score still reaches the high scores.
  7. Use the gamepad in every menu.
  8. The stress test's new length (Call 14): keep it, or ask for the full cadence back.
- [ ] **Step 4: Commit** `docs: decisions for plan 4`.
