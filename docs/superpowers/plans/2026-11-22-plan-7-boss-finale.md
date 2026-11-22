# Plan 7: Boss finale Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a boss is destroyed, a ~4 s full-speed finale plays (spin-off, roll or flip, left or right, then one
explosion with a single flash) before the run is Completed and the results screen appears.

**Architecture:** Core owns the finale. A pure `BossFinale` model gives the boss's pose over time from a
`FinaleChoice`; `World` starts it in `Destroy`, steps it, keeps the player safe and drives the player's car;
`MissionRun` gains `RunState.Finale`. The App only draws: roll/flip/spin of the boss outline, the bursts and the flash
from a new `BossExploded` event, and a skip hint. `Game` takes each crash from a `FinalePicker` that the App seeds from
the clock, so Core stays deterministic.

**Tech Stack:** C# / .NET 10, WPF (App), xUnit (tests/NightPursuit.Core.Tests).

**Spec:** `docs/superpowers/specs/2026-11-22-boss-finale-design.md` (approved 2026-11-22). Line-art rules:
`docs/superpowers/specs/2026-11-20-line-art-restyle-design.md`.

## Global Constraints

- Work on a new branch `plan-7-boss-finale` cut from `main` (c46d69a). Never merge or push without the owner's OK.
- Core has no WPF. Every rule runs per fixed step (`World.Step` = 1/120 s), so a finale plays the same however frames are split.
- Finale timings (targets, tunable by eye but tests use these constants): spin-out 1.2 s, off the road 1.3 s,
  explosion at 2.5 s, hold 1.5 s, total 4.0 s.
- Kinds: `SpinOff`, `Roll`, `Flip`; side −1 (left) or +1 (right); roll and flip turn 1 or 2 full turns.
- Never the same kind and side twice in a row within one `Game`.
- The flash: one per finale, peak 0.6 white, fading to 0 in 0.25 s, drawn under the HUD text.
- Skip hint copy, exactly: `SPACE OR ENTER: SKIP`.
- Combined line art only: black fills, `LineArt.Outline` / `LineArt.Detail` pens, `LineArt.Fade` with distance, the boss's
  own colour (`Neon.BossCar`) and look (Fin, Twin, Dome). No double images, trails or extra screen effects (owner, 2026-11-20).
- Nothing important only in sound.
- Visual checks only through the app's own `--snapshot`; never screen capture. About one exe launch for the whole plan.
- Test command: `dotnet test tests/NightPursuit.Core.Tests -c Release` (suite is 1262 tests on main); build
  `dotnet build NightPursuit.slnx -c Release` with 0 warnings.

## Review Focus

1. A boss killed by gunfire, missile or satellite (causes that normally drop a sliding `Wreck` and remove the car)
   still plays its finale and leaves no on-road `Wreck`. Test in Task 2.
2. Score, heat and spare cars after the finale equal today's, even when the player's oil or caltrops kill another
   enemy during the finale. Test in Task 2.
3. Pausing (or losing focus) during the finale resumes the finale, not normal play; R from that pause restarts a
   practice mission. Test in Task 3.
4. A fire key held through the kill does not skip the finale; only a new press does. Test in Task 3 (Core) and the
   key mapping in Task 4 uses non-repeat key-downs only.
5. Restarting the same mission gives a different crash, because the picker belongs to the `Game`, not the run. Test in Task 3.

---

### Task 1: The finale model

**Files:**
- Create: `src/NightPursuit.Core/Missions/BossFinale.cs` (FinaleKind, FinaleChoice, BossFinale)
- Create: `src/NightPursuit.Core/Missions/FinalePicker.cs`
- Test: `tests/NightPursuit.Core.Tests/BossFinaleTests.cs`

**Interfaces:**
- Produces:
  - `public enum FinaleKind { SpinOff, Roll, Flip }`
  - `public readonly record struct FinaleChoice(FinaleKind Kind, int Side, int Turns)`; `static FinaleChoice Default => new(FinaleKind.Roll, 1, 1)`.
    Side is −1 or +1; Turns is 1 or 2 for Roll/Flip and 0 for SpinOff (the constructor normalises SpinOff to 0).
  - `public sealed class BossFinale(FinaleChoice choice, int bossId, double distance, double lateral, double speed, double spinAngle)` with
    constants `SpinOutSeconds = 1.2`, `OffRoadSeconds = 1.3`, `ExplodeAt = 2.5`, `HoldSeconds = 1.5`, `TotalSeconds = 4.0`,
    `VergeLateral = 11.0`; properties `Choice`, `BossId`, `Elapsed`, `Distance`, `Lateral`, `Speed`, `SpinAngle`,
    `Roll`, `Pitch`, `Lift`, and `PrevRoll`, `PrevPitch`, `PrevLift`; `bool Exploded` (Elapsed ≥ ExplodeAt and not skipped),
    `bool Done`, `bool Skipped`; `bool Step(double dt)` (advances; returns true on the step it explodes);
    `void Skip()` (Done and Skipped at once, never Exploded); `double RollAt(double alpha)`, `PitchAt(alpha)`, `LiftAt(alpha)`
    (Prev + (cur − Prev) · alpha).
  - `public sealed class FinalePicker(IRng rng)` with `FinaleChoice Next()` and `FinaleChoice? Last`.

- [ ] **Step 1: Create the branch and write the failing tests** in `BossFinaleTests.cs`:
  - `Every_kind_ends_on_its_side_of_the_verge_and_comes_to_rest`: for each kind × side, a finale started at
    (distance 1000, lateral −side·3, speed 60, spin 0) stepped at `World.Step` to `ExplodeAt` has
    `Math.Sign(Lateral) == side`, `Math.Abs(Lateral) >= Track.RoadHalfWidth + 2`, `Speed == 0` (within 1e-6), and
    `Distance` never decreased on any step.
  - `Roll_turns_about_the_long_axis_and_flip_end_over_end`: at `ExplodeAt`, a Roll with Turns 2 has
    `Roll == side * 2 * Math.Tau` (1e-9) and `Pitch == 0`; a Flip with Turns 1 has `Pitch == Math.Tau` and `Roll == 0`;
    a SpinOff has both 0 throughout.
  - `Lift_is_only_off_the_road_and_never_above_one_and_a_half_metres`: Lift is 0 before `SpinOutSeconds` and after
    `ExplodeAt`, in (0, 1.5] at the middle of the off-road part for Roll and Flip, 0 throughout for SpinOff.
  - `A_roll_or_flip_trips_square_on`: for Roll and Flip, `SpinAngle` at `SpinOutSeconds` equals the start angle plus
    `side * Math.Tau` (one whole flat spin, so the car is square on when it trips) and stays there after.
  - `Step_reports_the_explosion_once_and_done_after_the_hold`: over the whole run `Step` returns true exactly once, on
    the step where `Elapsed` first reaches `ExplodeAt`; `Done` first becomes true when `Elapsed` reaches `TotalSeconds`.
  - `Skip_ends_it_without_an_explosion`: `Skip()` at 0.5 s gives `Done`, `Skipped`, not `Exploded`, and later `Step`
    calls return false and change nothing.
  - `Same_choice_and_start_give_the_same_poses`: two finales with equal inputs agree on every pose field at every step.
  - `Interpolation_is_between_the_last_two_steps`: after a step, `RollAt(0) == PrevRoll`, `RollAt(1) == Roll`, `RollAt(0.5)` halfway (same for Pitch, Lift).
  - `The_picker_never_repeats_kind_and_side_and_uses_every_combination`: 600 `Next()` calls from `new SeededRng(5)`:
    no two consecutive picks share (Kind, Side); all 6 (Kind, Side) pairs appear; every Roll/Flip has Turns 1 or 2 and
    both occur; every SpinOff has Turns 0.

  Run: `git checkout -b plan-7-boss-finale` then
  `dotnet test tests/NightPursuit.Core.Tests -c Release --filter FullyQualifiedName~BossFinaleTests`
  Expected: build FAILS (BossFinale, FinalePicker not defined).

- [ ] **Step 2: Implement `BossFinale` and `FinalePicker`.**
  The pose is a pure function of `Elapsed` and the start values (keep the start values; recompute from them each step,
  don't integrate drift). Speed falls linearly from the start speed to 0 at `ExplodeAt`; Distance is the integral of that
  speed (so it never decreases). Lateral eases (smoothstep) from the start lateral to `side * VergeLateral` by `ExplodeAt`.
  Spin-out: SpinAngle goes from the start angle to start + `side * Math.Tau` over `SpinOutSeconds` (ease-out), then holds for
  Roll and Flip; for SpinOff it keeps turning, slowing, to start + `side * 2.5 * Math.Tau` at `ExplodeAt`.
  Off the road (`SpinOutSeconds`..`ExplodeAt`, u from 0 to 1): Roll = `side * Turns * Math.Tau * smoothstep(u)`,
  Pitch = `Turns * Math.Tau * smoothstep(u)` for Flip, Lift = `1.2 * Math.Sin(Math.PI * u)`. The picker draws uniformly
  among the 5 (Kind, Side) pairs that differ from `Last` (all 6 when `Last` is null), then Turns 1 or 2 with equal odds.

- [ ] **Step 3: Run the tests.** Same filter. Expected: PASS (9 tests). Then the whole suite: Expected 1271 passed.

- [ ] **Step 4: Commit**
  `git add src/NightPursuit.Core/Missions/BossFinale.cs src/NightPursuit.Core/Missions/FinalePicker.cs tests/NightPursuit.Core.Tests/BossFinaleTests.cs`
  `git commit -m "feat(finale): boss finale model and crash picker"`

---

### Task 2: The world runs the finale

**Files:**
- Modify: `src/NightPursuit.Core/Simulation/World.cs` (Destroy, StepOnce, StepAgents, IsGone, DamagePlayer, WreckPlayer, StateHash)
- Modify: `src/NightPursuit.Core/Simulation/World.Heat.cs` (AddHeat, SetHeat, BustPlayer guards)
- Modify: `src/NightPursuit.Core/Simulation/Collisions.cs`, `src/NightPursuit.Core/Simulation/HazardContacts.cs` (skip the player; skip the finale boss)
- Modify: `src/NightPursuit.Core/Simulation/World.EnemyFire.cs` (no agent fire while the finale runs)
- Modify: `src/NightPursuit.Core/Simulation/GameEvent.cs` (two events)
- Create: `src/NightPursuit.Core/Simulation/FinaleAutopilot.cs`
- Modify: `src/NightPursuit.Core/Audio/SoundDirector.cs` (cues move)
- Test: `tests/NightPursuit.Core.Tests/FinaleWorldTests.cs`

**Interfaces:**
- Consumes: Task 1's `BossFinale`, `FinaleChoice`.
- Produces:
  - `World.Finale` (`BossFinale?`, public get, null until a boss dies) and `World.InFinale => Finale is not null`.
  - `World.FinaleSource` (`Func<FinaleChoice>`, default `() => FinaleChoice.Default`): called once, in `Destroy`, when the boss dies.
  - `public sealed record BossFinaleStarted(double Time, int VehicleId, FinaleKind Kind, int Side) : GameEvent(Time)`.
  - `public sealed record BossExploded(double Time, double Distance, double Lateral) : GameEvent(Time)`.
  - `internal static class FinaleAutopilot` with `static (PedalOutput Pedals, double Steer) Drive(World w, double laneCentre)`.
  - `World.ClearHostileFire()` (internal): the enemy-missile and hostile-tracer clearing that `MissionRun.Respawn` does today, moved here and called by both.

- [ ] **Step 1: Write the failing tests** in `FinaleWorldTests.cs`, using `TestWorlds.World(track: TestWorlds.Straight())`
  and `BossCar.Create(w.NextId(), def, distance, lateral, speed)` as `BossMissionTests` does:
  - `[Theory]` over `KillCause.Missile`, `KillCause.Gunfire`, `KillCause.Satellite`, and one cause that does not leave a
    wreck: `Destroying_the_boss_starts_a_finale_and_leaves_no_road_wreck`: after `Destroy`, `w.Finale` is not null with
    `BossId == boss.Id` and the choice from `FinaleSource`; the boss is still in `Agents` with `State == Wrecked`; no
    `Wreck` in `Hazards`; the "TEST DESTROYED  +5000" message and score as today; one `BossFinaleStarted` event.
  - `The_boss_explodes_once_onto_a_burning_verge_wreck_and_is_removed`: step to `ExplodeAt`: exactly one
    `BossExploded` event whose Distance/Lateral match the finale's; the boss is gone from `Agents`; one `Wreck` in `Hazards`
    at that place with Speed 0.
  - `Nothing_hurts_the_player_during_the_finale`: with the finale running, `DamagePlayer(50, "X")`, `WreckPlayer("X")`,
    `BustPlayer()`, `AddHeat(40, "X")`, `SetHeat(90, "X")` change nothing (armor, state, heat, `BustedPending`); a
    civilian placed overlapping the player and an oil slick under the player cause no damage, spin or grip loss over 1 s.
  - `Nothing_scores_or_raises_heat_during_the_finale` (Review Focus 2): a Runner driven onto the player's caltrops and an
    enemy killed by `Destroy` during the finale leave `Score` and `Heat.Value` exactly as at the kill.
  - `Agents_do_not_fire_and_fire_in_flight_is_cleared`: an enemy missile and a hostile tracer in flight at the kill are
    gone after the next step; a Gunner beside the player fires nothing during the finale.
  - `The_finale_boss_touches_nothing`: a civilian on the boss's path is never spun, damaged or destroyed.
  - `The_player_drives_itself_and_keeps_the_crash_in_view`: `[Theory]` over the boss starting 30 m ahead, 20 m behind
    and level (lateral +5) with both at 60 m/s, for each kind: the player's input is ignored (a full-left `ControlFrame`
    with Fire held changes nothing: no tracer, lateral stays within 0.5 m of its lane centre); at `ExplodeAt` the boss is
    40–150 m ahead; the player never gets within 30 m of the explosion point before `TotalSeconds`.
  - `Same_choice_same_finale` : two worlds from the same seed and choice give the same `StateHash()` at every step of the finale.
  - Sound: in `SoundDirectorTests`, `The_boss_explosion_sound_waits_for_the_explosion`: the boss kill step plays
    `SoundCue.Spin` and no `SoundCue.Explosion`; the `BossExploded` step plays `SoundCue.Explosion`; an ordinary enemy
    kill still plays `SoundCue.Explosion` at once.

  Run: `dotnet test tests/NightPursuit.Core.Tests -c Release --filter "FullyQualifiedName~FinaleWorldTests|FullyQualifiedName~SoundDirectorTests"`
  Expected: build FAILS (`World.Finale`, the events, `FinaleAutopilot` not defined).

- [ ] **Step 2: Implement.**
  - `Destroy`: for a `BossCar`, score and message as now; set `StateTimer = double.MaxValue`; do not add the `Wreck` or
    the removal even for wreck-leaving causes; create `Finale` from `FinaleSource()` and the boss's distance, lateral,
    speed and spin angle; publish `BossFinaleStarted`; call `ClearHostileFire()`. During the finale `Destroy` of any car
    awards 0 points.
  - `StepAgents`: the finale boss is moved by `Finale.Step(Step)` (write its Distance, Lateral, Speed, SpinAngle onto the
    car; `CapturePrev` already snaps Prev for the car; the finale keeps its own Prev roll/pitch/lift). On the explosion
    step: publish `BossExploded`, add the verge `Wreck` (the boss's width, length and `Seed = boss.Id`, Speed 0), set the
    boss's `StateTimer = 0` so `IsGone` removes it. After the boss is gone the finale keeps stepping until Done.
    No `StepAgentWeapons` for any agent while `InFinale`.
  - `StepOnce`: while `InFinale` and the player is driving, pedals and steer come from `FinaleAutopilot.Drive`
    (ignore `input` and the scheme); no `Armor <= 0` wreck, no `CheckSpeedTraps`, no gun trigger, no `StepHeat`, no
    `Busted.Update`. Guards (`if (InFinale) return;`) at the top of `DamagePlayer`, `WreckPlayer`, `BustPlayer`,
    `AddHeat`, `SetHeat`.
  - `Collisions.Resolve` and `HazardContacts`: skip every check that involves the player while `InFinale`, and leave
    the finale boss out of agent checks.
  - `FinaleAutopilot.Drive`: hold `laneCentre` (the lane centre nearest the player's lateral when the finale began,
    which the World stores at the start); aim for the boss 80 m ahead: desired speed
    `clamp(bossSpeed + 0.8 * (gap - 80), 0, Feel.MaxSpeed)`, where after the explosion bossSpeed is 0 and gap is to the
    explosion point; throttle or brake toward it. Tune the gains until the view test passes.
  - `StateHash`: mix in the finale's Elapsed, Distance, Lateral, Roll, Pitch, Lift when present.
  - `SoundDirector`: a `VehicleDestroyed` of `AgentKind.Boss` plays nothing; `BossFinaleStarted` plays `SoundCue.Spin`
    from the boss; `BossExploded` plays `SoundCue.Explosion` at its distance and lateral (as `From` does for a car).

- [ ] **Step 3: Run the tests.** Same filter. Expected: PASS. Then the whole suite. Expected: all pass except the two
  `BossMissionTests` that expect `Completed` right after the kill (`Destroying_the_boss_completes_the_mission` and the
  one near line 453), which Task 3 updates; no other failure.

- [ ] **Step 4: Commit**
  `git add -A src/NightPursuit.Core tests/NightPursuit.Core.Tests`
  `git commit -m "feat(finale): the world runs the boss finale and keeps the player safe"`

---

### Task 3: The run and the game

**Files:**
- Modify: `src/NightPursuit.Core/Missions/MissionRun.cs`
- Modify: `src/NightPursuit.Core/Control/GameAction.cs` (`ActionKind.SkipFinale`)
- Modify: `src/NightPursuit.Core/Session/Game.cs`, `src/NightPursuit.Core/Session/ScreenFlow.cs`
- Modify: `src/NightPursuit.Core/Audio/SoundDirector.cs` (live sound in Finale), `src/NightPursuit.Core/Audio/MusicSlots.cs` if it needs the state
- Modify: `tests/NightPursuit.Core.Tests/BossMissionTests.cs` (the two tests)
- Test: `tests/NightPursuit.Core.Tests/FinaleRunTests.cs`

**Interfaces:**
- Consumes: Task 2's `World.Finale`, `World.FinaleSource`, `World.ClearHostileFire()`.
- Produces:
  - `RunState.Finale` (between Playing and Completed in the enum).
  - `MissionRun(MissionDefinition def, FeelSettings feel, int seed, RunStart? start = null, Func<FinaleChoice>? nextFinale = null)`;
    `Rebuild` sets `World.FinaleSource = nextFinale ?? (() => FinaleChoice.Default)`.
  - `Game(…existing parameters…, FinalePicker? finales = null)`; null means `new FinalePicker(new SeededRng(seed))`.
    `Game.Finales` (the picker) for tests.

- [ ] **Step 1: Write the failing tests** in `FinaleRunTests.cs` (build runs with `RunAtBoss()`-style helpers copied from `BossMissionTests`):
  - `A_boss_kill_plays_the_finale_then_completes`: after `Destroy` and one update, `State == Finale`; after
    `BossFinale.TotalSeconds` more of updates, `Completed`; `Director.Completed` is true.
  - `The_mission_bonus_and_score_are_todays`: the same run with the boss destroyed, armor 64, score 700, through the
    finale into a `Game`: `LastBonus.Total` and the carried score equal what main gives today for the same values
    (compute them from `MissionBonus.For` with armor 64 and no car lost).
  - `A_mission_without_a_boss_completes_at_once` (unchanged behaviour).
  - `Skip_needs_a_new_press`: `Handle(SkipFinale)` in Finale makes it `Completed` at once with no `BossExploded`
    event; outside the finale `SkipFinale` does nothing.
  - `Pausing_in_the_finale_resumes_the_finale` (Review Focus 3): Pause → `Paused`, Pause → `Finale`; updates while
    paused don't advance `Finale.Elapsed`; in practice `Game.Handle(Restart)` from that pause restarts the mission.
  - `R_alone_does_not_restart_during_the_finale`: `Handle(Restart)` in Finale leaves the run in Finale and publishes the
    existing "PRESS P TO PAUSE, THEN R TO RESTART" message.
  - `A_wreck_in_the_kill_step_still_wins_and_keeps_the_spare`: player armor 0 and boss destroyed before one update:
    state Finale, player `State == Driving`, armor still 0, it is not wrecked again during the finale, `SpareCars` 3,
    no "BUSTED" announcement on screen; at Completed the bonus counts no car lost and armor 0.
  - `A_boss_killed_during_the_respawn_wait_respawns_at_once_then_plays_the_finale`: wreck the player, then `Destroy`
    the boss while `Respawning`; the next update respawns (one spare used, as today) and the state is Finale.
  - `Each_crash_comes_from_the_games_picker` (Review Focus 5): a practice `Game` with a picker on a fixed seed; kill
    the boss, note the finale's choice, restart the mission, kill it again: the second choice differs in (Kind, Side)
    and both equal the picker's successive `Next()` values.
  - `Focus_loss_in_the_finale_pauses_it`: `ScreenFlow.FocusLost()` during the finale goes to the pause screen and back resumes Finale.
  - `Nothing_new_appears_during_the_finale`: a mission with traffic and police (use `mission-02.json` loaded as
    `CampaignTests` does) with the boss killed: from the kill to Completed no agent or hazard is added except the verge
    `Wreck`.
  - `Engine_sound_runs_in_the_finale`: `SoundDirector`'s live state is not Silent in Finale.
  - Update the two `BossMissionTests`: after the kill, `State == Finale`; run the finale out; then `Completed`.

  Run: `dotnet test tests/NightPursuit.Core.Tests -c Release --filter "FullyQualifiedName~FinaleRunTests|FullyQualifiedName~BossMissionTests"`
  Expected: build FAILS (`RunState.Finale`, `ActionKind.SkipFinale`, the new constructor parameters not defined).

- [ ] **Step 2: Implement.**
  - `MissionRun.Update`: `case Finale: World.Advance(ControlFrame.Empty, frameSeconds, AfterFinaleStep)`;
    `AfterFinaleStep` returns true and sets Completed when `World.Finale.Done`.
  - `AfterPlayingStep`: when `Director.Completed`, enter Finale if `World.InFinale`, else Completed (a bossless mission).
  - `AfterRespawningStep`: if `World.InFinale`, call `Respawn()`, then `Director.Update(World)`, then enter Finale.
  - Entering Finale: if the player is Wrecked, set it Driving with `StateTimer = 0` (armor unchanged); clear
    `World.BustedPending` and a "BUSTED" announcement (add an internal `World.ClearAnnouncement()`).
  - Pause remembers the state it paused from (Playing or Finale) and returns to it. `CanRestart` is unchanged.
  - `Handle(SkipFinale)` in Finale: `World.Finale.Skip()` and Completed; elsewhere ignored (never forwarded to World).
  - `Game`: keep the picker; pass `Finales.Next` to every `MissionRun` it makes (start and restart).
  - `ScreenFlow`: treat Finale like Playing where it decides pausing (focus loss, the help pause).
  - `SoundDirector`: Finale counts as a live state, as Playing does.

- [ ] **Step 3: Run the tests.** Same filter, then the whole suite. Expected: all pass (about 1300).

- [ ] **Step 4: Commit**
  `git add -A src/NightPursuit.Core tests/NightPursuit.Core.Tests`
  `git commit -m "feat(finale): finale run state, skip, pause, and a crash picker per game"`

---

### Task 4: Drawing, input, snapshots and docs (App)

**Files:**
- Modify: `src/NightPursuit.App/Input/InputHub.cs`, `src/NightPursuit.App/Input/XInputPad.cs` (SkipFinale)
- Modify: `src/NightPursuit.App/App.xaml.cs` (the clock-seeded picker)
- Modify: `src/NightPursuit.App/Rendering/VehicleRenderer.cs`; create `src/NightPursuit.App/Rendering/FinaleRenderer.cs`
- Modify: `src/NightPursuit.App/Rendering/BossShapes.cs` (roof and underside outlines per look)
- Modify: `src/NightPursuit.App/Rendering/EffectList.cs`, `EffectRenderer.cs`, `GameView.cs` (bursts and flash)
- Modify: `src/NightPursuit.App/Rendering/Overlays.cs` (`DrawState`: the skip hint)
- Modify: `src/NightPursuit.App/Diagnostics/DiagnosticsRunner.cs` (finale scenes; stress)
- Modify: `README.md`, `docs/decisions.md`

**Interfaces:**
- Consumes: `World.Finale` (`RollAt`, `PitchAt`, `LiftAt`, `Exploded`, `BossId`, `Choice`), `BossExploded`,
  `BossFinaleStarted`, `RunState.Finale`, `ActionKind.SkipFinale`, `Game(…, FinalePicker? finales)`.

- [ ] **Step 1: Input and picker.** `InputHub.OnKeyDown`: Enter and Space (non-repeat only) also enqueue
  `GameAction(ActionKind.SkipFinale)` (Space keeps its menu Confirm and held Fire). `XInputPad`: a new press of A
  enqueues `SkipFinale`. `App.xaml.cs`: every `Game` it makes gets `new FinalePicker(new SeededRng(Environment.TickCount))`.

- [ ] **Step 2: Effects.** `EffectList.Absorb`: a boss `VehicleDestroyed` spawns nothing; `BossExploded` calls
  `SpawnBossKill(distance, lateral)` and starts the flash. `public double FlashAlpha` = `0.6 * (1 - age / 0.25)` for age
  in [0, 0.25), else 0, aged in `Update` (so it freezes behind screens like the explosions). `EffectRenderer.DrawFlash`
  fills the whole view with white at `FlashAlpha`; `GameView.Draw` calls it after `WindshieldRenderer.Draw` and before `Hud.Draw`.

- [ ] **Step 3: The boss in the finale.** `VehicleRenderer.DrawAll` passes the world's finale; the boss whose id is
  `Finale.BossId` is drawn while `!Finale.Exploded` even though it is Wrecked (no boss tag, no armor bar).
  `FinaleRenderer.Draw` draws it at the car's interpolated distance and lateral, raised by `LiftAt(alpha)` metres:
  - SpinOff and the spin-out: the existing `Body` spin drawing.
  - Roll: the rear outline (`Body` as upright) rotated by `RollAt(alpha)` (radians → degrees) about the body's centre.
  - Flip, algorithm: seen from behind, the car's side profile is a box `Length` long and `Height` high that turns by
    θ = `PitchAt(alpha)` about its centre (θ grows as the tail rises over the nose). Each face of the box is drawn only
    while it faces the camera — rear when cos θ > 0, underside when sin θ > 0, front (the rear outline upside down) when
    cos θ < 0, roof when sin θ < 0 — as a flat outline the car's width wide and as tall as that edge's vertical span on
    screen (Height·|cos θ| for rear/front, Length·|sin θ| for underside/roof), placed where that edge of the turned
    profile is. Draw back to front with black fills, so hidden faces never show.
  - `BossShapes` gains `Underside(BossLook)` (a plain panel with two axle lines and four tyre ends) and `Roof(BossLook)`
    (a windscreen line, with the look's fins, twin fins or dome as a simple shape).
  - All with `LineArt.Outline` / `LineArt.Detail` of `Neon.BossCar`, `LineArt.Fade(distance)`, the outline gaps as other cars.

- [ ] **Step 4: Skip hint.** `Overlays.DrawState`: in `RunState.Finale`, the line `SPACE OR ENTER: SKIP` in the same
  style and place as the existing state hints.

- [ ] **Step 5: Snapshot scenes.** `DiagnosticsRunner`: scenes `finale-spinoff`, `finale-roll`, `finale-flip`; options
  `--finale-side left|right` (default right), `--finale-turns 1|2` (default 1) and `--finale-at` a comma list of seconds
  into the finale (default `0.6,1.6,1.9,2.52,3.2`), each written as its own file the way the `--damage` list is. The scene
  puts the player on open road at 60 m/s with the boss 40 m ahead, destroys it with the named `FinaleChoice` (set
  `World.FinaleSource`), and runs the finale to the named moment while feeding `view.Effects` (so 2.52 shows the
  flash). `--stress`: add `RunState.Finale` to `StressStates`, and in those frames kill the boss for real so a finale is
  running when they draw.

- [ ] **Step 6: Build and run the tests.** `dotnet build NightPursuit.slnx -c Release` Expected: 0 warnings, 0 errors.
  `dotnet test tests/NightPursuit.Core.Tests -c Release` Expected: all pass.

- [ ] **Step 7: Snapshot check (the one exe launch).** One `--snapshot` run with
  `--scene finale-spinoff,finale-roll,finale-flip --size 1280x720,960x540` (both sides via a second `--finale-side`
  value only if the tool takes a list; otherwise right only and left is covered by the tests). Look at every file: the
  boss's look is recognisable at each moment, black fills hide hidden faces, it sits off the road on the verge, the
  flash frame is pale and the HUD text readable on it, and the hint shows. Make a contact sheet for the owner.

- [ ] **Step 8: Docs.** README: under the controls, one line "After a boss is beaten its crash plays for a few
  seconds; Space, Enter or pad A skips it." `docs/decisions.md`: the next numbers (D-series) for the builder's calls in
  the spec and every ruling made while building, and a "Checks only you can do (boss finale)" list: watch each kind
  of crash in play; the flash is comfortable; 4 s doesn't drag; the boss stays recognisable while it rolls and flips.

- [ ] **Step 9: Commit**
  `git add -A src README.md docs`
  `git commit -m "feat(finale): draw the boss crash, the flash and the skip hint; snapshot scenes"`
