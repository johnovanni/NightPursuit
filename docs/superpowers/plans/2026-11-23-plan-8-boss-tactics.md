# Plan 8: Boss tactics, part 1 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An engaged boss never follows you below 90 mph, can escape (mission failed, a spare car lost), and when you
hang back below 45 mph it picks a tactic (run, shoot back, drop hazards, reverse ram) that gets smarter mission by mission.

**Architecture:** A per-boss `BossMind` in Core/Drivers sits between the phase drivers and the physics: `BossDriver`
asks the phase for its intent as now, then the mind applies the 90 mph floor or, during a tactic, replaces the intent.
A `BossEscapeWatch` in Core/Missions counts the escape; `MissionRun` gains `RunState.Escaped`, `Game` a
`GameStage.MissionFailed`, `ScreenFlow` a `Screen.MissionFailed`. The reverse ram gets its own contact path in
`Collisions`. The App draws the escape banner, the failed screen and the boss's reversing lights.

**Tech Stack:** C# / .NET 10, WPF (App), xUnit (tests/NightPursuit.Core.Tests).

**Spec:** `docs/superpowers/specs/2026-11-23-boss-tactics-design.md` (approved 2026-11-23).

## Global Constraints

- Branch `plan-8-boss-tactics` from `main` (3b01a66). Never merge or push without the owner's OK.
- Core has no WPF; rules run per fixed step (`World.Step`); all randomness from `World.Rng` (via `WorldView.Rng`), so StateHash stays repeatable.
- Speeds: floor **40 m/s (90 mph)**; hanging back below **20 m/s (45 mph)**; running pace by cunning **56, 57, 59, 60, 62, 63 m/s** (125→140 mph); reverse up to **11 m/s (25 mph)** backwards.
- Escape: warning beyond **600 m** ahead; escaped after **20 s** beyond 600 m or at **1,200 m**. Counts only while the run is Playing (not respawning, paused or in the finale) and the boss is engaged.
- Escape cost: mission restarts from its briefing; you **keep the score you have at the escape** (points earned in the failed attempt count) and the spare cars you have then, minus **one**; with no spare car left at the escape it is game over (normal game-over and high-score flow with the score as it stands).
- Reverse ram: only within **80 m** ahead, only above **40 %** health and unshielded; costs you **10** armor ("THE BOSS BACKED INTO YOU") and the boss **4 %** of full health; never your fault (no blame, no heat); always followed by a run.
- Extra car every **25,000 points** of total score (25,000, 50,000, …), from any source including the mission bonus; at most **5** spare cars; cue "EXTRA CAR" (message line in play, results screen for the bonus). Same in practice.
- Cunning 1–6 from the boss block `"cunning"` (default 1); missions 01–06 get 1–6; practice grounds 1. Reaction after hanging back: **3.0, 2.5, 2.0, 1.5, 1.0, 1.0 s**.
- On-screen cues (no sound needed): "BOSS MAKING A RUN FOR IT", "BOSS SHOOTING BACK", "BOSS REVERSING" (+ reversing lights 1 s before it moves backwards), "BOSS GETTING AWAY" box with distance and countdown, "MISSION FAILED" screen.
- Combined line art for the reversing lights (Detail pen, white, depth fade).
- Test command: `dotnet test tests/NightPursuit.Core.Tests -c Release` (1348 on main). Build `dotnet build NightPursuit.slnx -c Release`, 0 warnings.
- About one exe launch for the whole plan (snapshots); never screen capture.

## Review Focus

1. A player who stops or crawls can no longer keep an engaged boss within the guns' 380 m for long, in any phase (the original complaint). Test in Task 1.
2. A boss that escapes during a finale, a respawn or a pause: impossible (the watch only counts in Playing). Test in Task 2.
3. A reverse ram can never destroy the boss by itself, and a boss killed by something else while reversing still gets a proper finale. Test in Task 3.
4. A player driving flat out always catches a running boss (its pace is below your top speed even on the F12 tuning panel's lower top speeds: the run is capped at your current top speed − 6 m/s). Test in Task 3.
5. An escape with 0 spare cars left is game over, not a negative car count. Test in Task 2.

---

### Task 1: Cunning, the speed floor, no waiting far ahead

**Files:**
- Modify: `src/NightPursuit.Core/Missions/BossDefinition.cs` (add `int Cunning = 1`), `MissionLoader.cs` (`ReadBoss`: optional `cunning` 1–6; error "boss.cunning must be a whole number from 1 to 6")
- Modify: `content/missions/mission-01.json` … `mission-06.json` (`"cunning": 1` … `6` in the boss block)
- Create: `src/NightPursuit.Core/Drivers/BossMind.cs` (this task: the class with the floor only)
- Modify: `src/NightPursuit.Core/Vehicles/BossCar.cs` (`public BossMind Mind { get; } = new();`, reset in `ReturnTo`)
- Modify: `src/NightPursuit.Core/Drivers/BossDriver.cs`
- Test: `tests/NightPursuit.Core.Tests/BossTacticsTests.cs`

**Interfaces:**
- Produces: `BossDefinition.Cunning`; `public sealed class BossMind` with `public const double FloorSpeed = 40, HangBackSpeed = 20;` and `public DriveIntent Apply(BossCar b, WorldView view, DriveIntent phase, double dt)` (Task 3 extends it); `BossCunning` static helpers `ReactSeconds(int)`, `RunPace(int)`.

- [ ] **Step 1: Create the branch and write the failing tests:**
  - `Cunning_is_read_and_checked`: `"cunning": 4` loads as 4; absent loads as 1; 0, 7 and 2.5 throw with the message above; the six campaign files load as 1…6 and the practice grounds as 1.
  - `An_engaged_boss_never_follows_you_below_90_mph` (Review Focus 1): a `[Theory]` over each phase behaviour (Runner, Blocker, Pursuer, Gunner, Dropper, Armored): an engaged boss 60 m ahead, the player at 0 m/s for 5 s of steps (mind tactics not yet built, so only the floor acts): the boss's speed never falls below 40 − 1e-9 m/s and the gap grows past 380 m within 15 s.
  - `Above_the_floor_phases_still_pace_you`: the player at 55 m/s; a Runner-phase boss 70 m ahead keeps 55–75 m ahead over 5 s (unchanged behaviour).
  - `Caltrops_and_spins_still_take_it_below_the_floor`: with `SlowLeft > 0` the target is the floored speed × `CaltropSlowFactor`; a spinning boss isn't driven by the mind.
  - `A_boss_far_ahead_no_longer_waits_for_you`: engaged, 700 m ahead, player at 10 m/s: its target speed ≥ 40.
  Run: `git checkout -b plan-8-boss-tactics` then `dotnet test tests/NightPursuit.Core.Tests -c Release --filter FullyQualifiedName~BossTacticsTests` — Expected: build FAILS (Cunning, BossMind missing).

- [ ] **Step 2: Implement.** `BossDriver.Decide`: after `PassWhenLeftBehind`, `intent = b.Mind.Apply(b, view, intent, dt)` (Task 1: `TargetSpeed = Max(TargetSpeed, FloorSpeed)`), then the caltrops factor, then the far-behind catch-up; delete the far-ahead wait (`LeashAhead`, `LeashWait`). The not-yet-engaged easing-in is unchanged (no floor). Add the JSON fields.

- [ ] **Step 3: Run the tests.** Filter, then the whole suite. Expected: the new tests pass; fix any existing boss test that relied on the boss slowing with a slow player, each fix logged as a ledger Ruling.

- [ ] **Step 4: Commit** `git add src/NightPursuit.Core tests content/missions && git commit -m "feat(boss): cunning per mission, a 90 mph floor, no waiting far ahead"`

---

### Task 2: Getting away and its cost; extra cars

**Files:**
- Create: `src/NightPursuit.Core/Missions/BossEscapeWatch.cs`, `src/NightPursuit.Core/Session/ExtraCars.cs`
- Modify: `Simulation/World.cs` and `World.EnemyFire.cs` (every score change goes through `AddScore`), `Vehicles/PlayerCar.cs` (`CarsLost`), `Session/MissionBonus.cs` callers (the clean bonus uses `CarsLost == 0`), `App/Screens/ResultScreen.xaml.cs` ("EXTRA CAR" line), `MissionRun.cs` (`RunState.Escaped`; the watch stepped in `AfterPlayingStep`; `Respawn` counts `CarsLost`), `Session/GameStage.cs` (`MissionFailed`), `Session/Game.cs`, `Session/ScreenFlow.cs` (`Screen.MissionFailed`, `OnMissionFailed`), `Audio/MusicSlots.cs` (MissionFailed uses the GameOver slot)
- Test: `tests/NightPursuit.Core.Tests/BossEscapeTests.cs`

**Interfaces:**
- Produces: `public sealed class BossEscapeWatch` with `public const double WarnBeyond = 600, EscapeAt = 1200, Seconds = 20;`, `bool Warning`, `double SecondsLeft`, `double Gap`, `bool Update(World w, BossCar? boss, double dt)` (true when escaped); `MissionRun.Escape` (the watch); `RunState.Escaped`; `GameStage.MissionFailed`; `Screen.MissionFailed`; `Game.RetryMission()`; `public static class ExtraCars` with `public const int Every = 25000, MaxSpare = 5;` and `static int Earned(int scoreBefore, int scoreAfter)` (thresholds crossed); `World.AddScore(int points)`; `GameEvent ExtraCarEarned`; `PlayerCar.CarsLost`; `Game.LastExtraCars` (cars the mission bonus earned, for the results screen).

- [ ] **Step 1: Write the failing tests:**
  - `ExtraCars_Earned_counts_25000_thresholds`: (24_999→25_000) = 1; (0→24_999) = 0; (20_000→80_000) = 3; (50_000→50_000) = 0.
  - `Crossing_25000_in_play_gives_a_car_and_says_so`: world score 24_900, a 600-point kill → `SpareCars` +1, a `Message` "EXTRA CAR" (Tone.Good) and an `ExtraCarEarned` event.
  - `Spare_cars_never_go_above_5`: 5 spare cars, cross 25,000 → still 5.
  - `The_mission_bonus_can_earn_a_car`: finish a mission at 24,000 with a 2,000 bonus → `Game.SpareCars` +1, `LastExtraCars == 1`.
  - `A_car_lost_and_a_car_earned_still_loses_the_clean_bonus`: wreck one car and earn one in the same mission → `LastBonus.NoCarLost == false` (today it compares spare cars at start and end, which an earned car would fool; use `CarsLost`).
  - `The_warning_starts_beyond_600_m_and_the_boss_escapes_after_20_s`: engaged boss 601 m ahead held there: `Warning` true, `SecondsLeft` counts 20 → 0, escaped on the step it reaches 0; back under 600 m resets it.
  - `Reaching_1200_m_is_an_escape_at_once`.
  - `It_only_counts_while_playing` (Review Focus 2): no count while Respawning, Paused or in the Finale, nor before the boss is engaged.
  - `A_short_stop_never_sets_off_the_warning`: cunning-1 boss 70 m ahead at 50 m/s, player stops 3 s then drives `Gas` flat out with the test autopilot steer: `Warning` never true over 90 s. (Uses the Task 3 run; until Task 3 lands the boss drives at the floor, which is slower, so the test is valid now and stays valid.)
  - `An_escape_fails_the_mission_keeps_the_points_and_costs_a_car`: a `Game` with score 1000 and 3 spare cars at mission start; 500 points earned and one car wrecked (2 spare cars left) during the run; an escape → `Stage == MissionFailed`, `Score == 1500`, `SpareCars == 1`; `RetryMission()` → Briefing for the same `MissionIndex`, and the next run starts with score 1500 and 1 spare car.
  - `An_escape_with_no_spare_cars_is_game_over` (Review Focus 5): 0 spare cars left at the escape (even if there were some at the mission's start) → `GameOver`, `Score` = the score at the escape, `SpareCars` 0.
  - `ScreenFlow_shows_mission_failed_and_retries_on_enter`; practice: R restarts too; `MusicSlots` picks the GameOver track for it.
  Run: `--filter FullyQualifiedName~BossEscapeTests` — Expected: build FAILS.

- [ ] **Step 2: Implement.** The watch reads `boss.Distance - player.Distance`; `MissionRun.AfterPlayingStep` steps it after the director and before the wreck check; escaped → publish `Message("THE BOSS ESCAPED", Tone.Bad)`, `State = Escaped`. Extra cars: `World.AddScore` replaces the two `Score +=` lines and awards `ExtraCars.Earned` cars (capped at `MaxSpare`), with the message and event; `Game.Update` on Completed awards the cars the bonus crosses into `_spareCars` (capped) and sets `LastExtraCars`; the results screen shows "EXTRA CAR" under the bonus lines when it is above 0. `Game.Update`: on Escaped, `_score = Run.World.Score` (no mission bonus); if `Run.World.Player.SpareCars <= 0` → GameOver (via the same path as a wreck on the last car), else `_spareCars = Run.World.Player.SpareCars - 1` and `MissionFailed`. `RetryMission()`: MissionFailed → Briefing (Run = null). ScreenFlow: Update goes to `Screen.MissionFailed`; Confirm → `RetryMission()` and Briefing; R in practice restarts.

- [ ] **Step 3: Run the tests.** Filter, then the suite. Expected: all pass.

- [ ] **Step 4: Commit** `git commit -m "feat(boss): a boss can escape, failing the mission and costing a car; extra car every 25,000 points"`

---

### Task 3: The boss mind's tactics and the reverse ram

**Files:**
- Modify: `src/NightPursuit.Core/Drivers/BossMind.cs`
- Modify: `src/NightPursuit.Core/Simulation/Collisions.cs` (`CarVsCar`: the reverse-ram path before the rear-end path)
- Modify: `src/NightPursuit.Core/Drivers/BossPursuitDriver.cs` (gives up getting behind you, see below)
- Test: `tests/NightPursuit.Core.Tests/BossTacticsTests.cs`

**Interfaces:**
- Produces: `public enum BossTactic { None, Run, ShootBack, DropHazards, ReverseRam }`; `BossMind.Tactic`, `BossMind.RamStage` (`Brake, Lights, Reverse`), `BossMind.HangingBack` (seconds), `bool BossCar.ReversingLights` (Lights or Reverse stage); `internal void World.BossBackedIntoPlayer(BossCar b)`.

- [ ] **Step 1: Write the failing tests** (deterministic rng; one boss on a straight road):
  - `Hanging_back_is_noticed_after_the_cunning_reaction_time`: for cunning 1–6 the tactic starts on the step `HangingBack` reaches 3.0/2.5/2.0/1.5/1.0/1.0 s; speeding back above 20 m/s before that resets it; never while the boss is behind you or not engaged.
  - `Cunning_1_always_runs`; `Cunning_2_uses_its_phase_weapon_or_runs` (Gunner phase → ShootBack or Run; Dropper → DropHazards or Run; other phases → Run); `Cunning_3_may_ram_but_never_when_hurt` (at 39 % health never ReverseRam; beyond 80 m never ReverseRam); `Cunning_4_picks_what_fits` (≤ 40 % → Run; your armor ≤ 35 and guns → ShootBack; else its weapon; else a ram if close, else Run); `Cunning_5_runs_after_shooting_back_or_dropping`; `Cunning_6_never_repeats_a_tactic_that_failed` (a tactic fails when your armor did not drop during it).
  - `A_run_goes_at_its_pace_and_ends_when_you_close_in`: target = `RunPace(cunning)`, capped at your top speed − 6 m/s (Review Focus 4: with `Feel.MaxSpeedMph = 100` a running boss is still caught); ends when you are within 200 m; announces "BOSS MAKING A RUN FOR IT" once.
  - `Shoot_back_and_drop_lift_the_floor_for_up_to_6_s`: the phase's own speed applies (may be under 40 m/s); after 6 s (or once you stop hanging back) the floor returns; cues announced.
  - `The_reverse_ram_brakes_shows_lights_reverses_and_runs`: stages in order; lights for 1.0 s at a stop; then speed goes negative down to −11 m/s steering to your lateral; contact → you −10 armor with "THE BOSS BACKED INTO YOU", boss −4 % of `MaxHealth`, no `BlamePlayer`, heat unchanged, then a Run; no contact within 5 s or gap over 120 m → Run.
  - `A_ram_never_destroys_the_boss` (Review Focus 3): a boss at 41 % health rams: health ≥ 37 %, not destroyed; `DamageBoss` phase floors still apply.
  - `A_boss_killed_while_reversing_gets_its_finale`: destroyed at −8 m/s: `Finale` starts (start speed ≥ 0) and plays to Completed.
  - `The_pursuer_gives_up_getting_behind_you`: Pursuer phase dropping back, player hangs back so the boss is still level after 3 s → it passes ahead and, at cunning ≥ 4, runs.
  - `Tactics_are_repeatable`: two worlds, same seed, same inputs → same `StateHash()` through a tactic.
  Run: filter — Expected: FAIL (tactics missing).

- [ ] **Step 2: Implement `BossMind.Apply`.** Per step while engaged and driving: update `HangingBack`; when it reaches `ReactSeconds(cunning)` and no tactic is active, choose by the cunning table (random draws from `view.Rng`); run the active tactic, else apply the floor. Announce cues with `World.Announce(…, 2.5, urgent: true)` via a published `Message` (the mind can't reach `World`; add `BossTacticStarted(Time, BossId, BossTactic)` event and let `World.StepAgents` announce it — or pass an announce callback; builder's choice, ledgered). Reverse: return a negative `TargetSpeed`; `BossDriver` no longer clamps to ≥ 0 while `Tactic == ReverseRam`. `CarVsCar`: if `a is BossCar { Mind.Tactic: BossTactic.ReverseRam } b && b.Speed < 0` and it overlaps from in front → `w.BossBackedIntoPlayer(b)` (damage both, separate the cars, boss speed 0, mind → Run) and return.

- [ ] **Step 3: Run the tests.** Filter, then the suite. Expected: all pass.

- [ ] **Step 4: Commit** `git commit -m "feat(boss): tactics when you hang back - run, shoot back, drop, reverse ram - smarter each mission"`

---

### Task 4: On screen: escape box, failed screen, reversing lights; snapshots and docs

**Files:**
- Modify: `src/NightPursuit.App/Rendering/Overlays.cs` (`DrawEscape`, drawn where `DrawBusted` is, not both at once: busted wins), `Rendering/GameView.cs`
- Modify: `src/NightPursuit.App/Screens/ResultScreen.xaml.cs`, `Screens/ScreenHost.cs`, `GameLoop.cs` (`IsResultScreen` includes MissionFailed)
- Modify: `src/NightPursuit.App/Rendering/VehicleRenderer.cs` (reversing lights)
- Modify: `src/NightPursuit.App/Diagnostics/DiagnosticsRunner.cs` (scenes `escape`, `reversing`), `Diagnostics/ScreenSnapshots.cs` (screen `mission-failed`), stress states include `Escaped`
- Modify: `README.md`, `docs/decisions.md`

- [ ] **Step 1: The escape box.** Same box style as the busted countdown: `BOSS GETTING AWAY` (amber, 44) and `{gap:0} M AHEAD — {SecondsLeft:0} S` (white, 18) while `run.Escape.Warning`.
- [ ] **Step 2: The failed screen.** `MISSION FAILED`, then `THE BOSS ESCAPED`, `YOU LOSE A SPARE CAR`, `SCORE {Score:00000}   SPARE CARS {SpareCars}`, key line `ENTER  TRY AGAIN` (practice: the practice keys).
- [ ] **Step 3: Reversing lights.** While `BossCar.ReversingLights`: two short white bars low on the rear, either side, Detail pen, faded with distance.
- [ ] **Step 4: Snapshots.** Scenes `escape` (boss 700 m ahead, 12 s left) and `reversing` (boss 30 m ahead in the Lights stage); screen `mission-failed`, and a results screen with "EXTRA CAR". Build (0 warnings), suite green, then one snapshot launch at 1280x720 and 960x540, checked by eye and sent to the owner.
- [ ] **Step 5: Docs.** README "In play": the escape rule and the boss's tactics in two lines. `docs/decisions.md`: D191 onward for the builder's calls and rulings; "Checks only you can do (boss tactics)".
- [ ] **Step 6: Commit** `git add src README.md docs && git commit -m "feat(boss): escape warning, mission failed screen, reversing lights; snapshots and docs"`
