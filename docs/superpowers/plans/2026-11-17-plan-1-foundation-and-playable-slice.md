# Night Pursuit Plan 1: Foundation and Playable Slice. Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the real game's foundation and one playable mission (without a boss): a tested Core
library and a WPF app where you drive with turn-to-aim steering, fight Runners, Blockers and
Pursuers with machine guns, oil and ramming, and see wrecks block the road.

**Architecture:** `NightPursuit.Core` (no WPF) holds every rule: track, road view, physics,
vehicles, drivers, weapons, hazards, collisions, events, mission director. It advances in fixed
1/120 s steps from a seed, so it is deterministic. `NightPursuit.App` (WPF) reads input, calls
`MissionRun.Update`, and draws with `DrawingContext`. `NightPursuit.Core.Tests` (xUnit) pins the rules.

**Tech Stack:** C# / .NET 10, WPF (`net10.0-windows`), xUnit 2.9.3, System.Text.Json, XInput via P/Invoke.

**Spec:** `docs/superpowers/specs/2026-11-17-night-pursuit-design.md`

**The four plans:**
1. **Foundation and playable slice** (this plan)
2. Full arsenal, all six enemy types and the boss (WPN-3…8, ENM-3/4, boss phases)
3. Civilians, police, heat and busted (CIV, HT)
4. Six missions, all screens, settings, high scores, persistence (MSN, HUD-5, NFR-4)

**Reference:** the prototype `spikes/ControlsLab` (read-only) already has working versions of
the road geometry, throttle schemes, turn-to-aim steering, renderer and diagnostics. Tasks
point to it where porting is the fastest correct path. It is not referenced by any project.

## Global Constraints

- Target frameworks: Core and Tests `net10.0`; App `net10.0-windows` with `<UseWPF>true</UseWPF>`.
- `Directory.Build.props`: `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<Version>0.1.0</Version>`.
- Core references no WPF types and never calls `DateTime.Now`, `Stopwatch`, or `new Random()`. Time arrives as `dt`, and randomness comes from `IRng`.
- Units inside Core: metres, seconds, m/s, radians. Only the HUD shows mph (`Units.MphPerMs = 2.23694`).
- Simulation step is exactly `1.0 / 120` s. One `Advance` call runs at most 0.25 s of simulation (30 steps).
- Nothing important may depend on sound. There is no audio in this plan.
- Original work only: no names, art or music from Spy Hunter, Speed Freak or any other game.
- No screen-capture scripts. Visual checks use the app's `--snapshot`, `--stress` and `--perf` options.
- **Commits:** the owner approves committing before execution starts. Commit messages end with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never push.
- Test command (from repo root): `dotnet test tests/NightPursuit.Core.Tests`.

## Review Focus

1. **A long frame stall** (window dragged, alt-tab, debugger pause) passes a 2 s `frameSeconds`.
   The simulation must run at most 0.25 s and not tunnel or blow up. *(Test in Task 7.)*
2. **The window loses focus while a key is held.** The car must stop steering and accelerating,
   not keep going forever. *(Manual check in Task 12: `KeyboardReader.Read(windowActive: false)` returns an empty frame.)*
3. **Very small or very wide windows** (minimised to 0×0, 320×200, 2560×600) must render without exceptions. *(Stress sizes in Task 16.)*
4. **A broken or missing mission file** must show a readable error message naming the file
   and field, not crash. *(Loader tests in Task 11; the App shows the message in Task 12.)*
5. **Fast tracers tunnelling through cars:** a tracer moves 2.2–2.75 m per step, and a car is
   4.4 m long. Hit tests must be swept between steps, so a tracer can't jump over a car. *(Test in Task 8.)*

---

## File structure

```
NightPursuit.slnx
Directory.Build.props
content/missions/mission-01.json
src/NightPursuit.Core/
  NightPursuit.Core.csproj
  Common/      Units.cs, MathX.cs, IRng.cs
  Road/        TrackProfile.cs, Track.cs, RoadPoint.cs, RoadView.cs, RoadPose.cs, RoadMotion.cs
  Control/     FeelSettings.cs, ControlFrame.cs, GameAction.cs, PedalOutput.cs, IThrottleScheme.cs,
               RampPedalScheme.cs, SpeedLeverScheme.cs, SteeringInput.cs
  Vehicles/    VehicleStats.cs, Vehicle.cs, PlayerCar.cs, AgentCar.cs, CarPhysics.cs, DriveIntent.cs
  Drivers/     IDriver.cs, WorldView.cs, DriverHelpers.cs, RunnerDriver.cs, BlockerDriver.cs, PursuerDriver.cs
  Weapons/     Weapon.cs, MachineGuns.cs, OilDropper.cs, Loadout.cs, Tracer.cs
  Hazards/     Hazard.cs, OilSlick.cs, Wreck.cs
  Simulation/  World.cs, Collisions.cs, GameEvent.cs
  Missions/    MissionDefinition.cs, MissionLoader.cs, MissionDirector.cs, MissionRun.cs
src/NightPursuit.App/
  NightPursuit.App.csproj, App.xaml(.cs), MainWindow.xaml(.cs), GameLoop.cs
  Input/       KeyboardReader.cs, XInputPad.cs, InputHub.cs
  Rendering/   Neon.cs, LineBatch.cs, Projector.cs, SkyRenderer.cs, RoadRenderer.cs,
               VehicleRenderer.cs, HazardRenderer.cs, EffectRenderer.cs, Hud.cs, Mirror.cs, Radar.cs, GameView.cs
  Diagnostics/ DiagnosticsRunner.cs, Autopilot.cs
  Debug/       TuningPanel.cs   (compiled only in Debug)
tests/NightPursuit.Core.Tests/
  NightPursuit.Core.Tests.csproj, TestWorlds.cs, one *Tests.cs file per Core area
```

---

### Task 1: Solution skeleton and common types

**Files:**
- Create: `NightPursuit.slnx`, `Directory.Build.props`, `src/NightPursuit.Core/NightPursuit.Core.csproj`,
  `tests/NightPursuit.Core.Tests/NightPursuit.Core.Tests.csproj` (copy package versions from
  `C:\repos\ASLCoach\tests\ASLCoach.Core.Tests\ASLCoach.Core.Tests.csproj`), `src/NightPursuit.Core/Common/Units.cs`, `MathX.cs`, `IRng.cs`
- Test: `tests/NightPursuit.Core.Tests/CommonTests.cs`

**Interfaces:**
- Produces: `Units.MphPerMs`, `Units.MphToMs(double)`, `Units.MsToMph(double)`;
  `MathX.MoveToward(double value, double target, double step)`;
  `interface IRng { double NextDouble(); int Next(int maxExclusive); }`; `sealed class SeededRng(int seed) : IRng` (wraps `System.Random(seed)`).

- [ ] **Step 1: Create the solution, props and both projects; add the Core reference to Tests.** Run `dotnet build`. Expected: build succeeded.
- [ ] **Step 2: Write failing tests**

```csharp
[Fact] public void Mph_round_trips() => Assert.Equal(155, Units.MsToMph(Units.MphToMs(155)), 9);
[Fact] public void MoveToward_does_not_overshoot() { Assert.Equal(1.0, MathX.MoveToward(0.9, 1.0, 0.5)); Assert.Equal(0.4, MathX.MoveToward(0.9, 0.0, 0.5), 9); }
[Fact] public void SeededRng_is_repeatable() { var a = new SeededRng(7); var b = new SeededRng(7); for (int i = 0; i < 100; i++) Assert.Equal(a.NextDouble(), b.NextDouble()); }
```
- [ ] **Step 3: Run** `dotnet test tests/NightPursuit.Core.Tests`. Expected: FAIL (types missing).
- [ ] **Step 4: Implement the three files.**
- [ ] **Step 5: Run tests.** Expected: 3 passed.
- [ ] **Step 6: Commit** `feat: solution skeleton and common types`, adding the `.gitignore` already in the repo.

### Task 2: Track

**Files:**
- Create: `src/NightPursuit.Core/Road/TrackProfile.cs`, `Road/Track.cs`
- Test: `tests/NightPursuit.Core.Tests/TrackTests.cs`

**Interfaces:**
- Produces:
  - `sealed record TrackProfile(int Seed, double MinRadius = 180, double MaxRadius = 700, double HillAmplitude = 16, int Segments = 4000)`
  - `sealed class Track` with constants `SegmentLength = 5.0`, `RoadHalfWidth = 7.5`, `LaneLine = 2.5`, `GuardRail = 12.0`, `static readonly double[] LaneCentres = { -5, 0, 5 }`
  - Constructors: `Track(TrackProfile p)`, `static Track FromSegments(double[] curvature, double[]? height = null)`
  - Members: `double Length`, `double Wrap(double d)`, `double CurvatureAt(double d)` (1/m, positive bends right), `double HeightAt(double d)` (interpolated), `double SlopeAt(double d)`, `double HeadingAt(double d)`

Port the generator from `spikes/ControlsLab/Track.cs`: bends with radius in [MinRadius, MaxRadius] eased over 25% of each end, straights of 20–90 segments, the first 60 and last 60 segments straight, and hills of `HillAmplitude` split 11/16 and 5/16 across the two sine terms.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Same_seed_same_track() { var a = new Track(new TrackProfile(3)); var b = new Track(new TrackProfile(3)); for (double d = 0; d < a.Length; d += 37) Assert.Equal(a.CurvatureAt(d), b.CurvatureAt(d)); }
[Fact] public void Radii_stay_within_profile() { var t = new Track(new TrackProfile(5)); for (double d = 0; d < t.Length; d += 5) { double k = Math.Abs(t.CurvatureAt(d)); Assert.True(k == 0 || 1 / k >= 180 - 1e-9); } }
[Fact] public void Start_and_end_are_straight() { var t = new Track(new TrackProfile(5)); Assert.Equal(0, t.CurvatureAt(10)); Assert.Equal(0, t.CurvatureAt(t.Length - 10)); }
[Fact] public void Wrap_handles_negative() { var t = Track.FromSegments(new double[100]); Assert.Equal(495, t.Wrap(-5), 9); }
[Fact] public void Heading_integrates_curvature() { var k = Enumerable.Repeat(0.01, 100).ToArray(); var t = Track.FromSegments(k); Assert.Equal(0.01 * 50, t.HeadingAt(50), 9); }
```
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat: seeded track with bends and hills`.

### Task 3: Road view and straight-line motion

**Files:**
- Create: `Road/RoadPoint.cs`, `Road/RoadView.cs`, `Road/RoadPose.cs`, `Road/RoadMotion.cs`
- Test: `tests/NightPursuit.Core.Tests/RoadGeometryTests.cs`

**Interfaces:**
- Consumes: `Track` (Task 2).
- Produces:
  - `readonly record struct RoadPoint(double X, double F, double Y, double Heading)`: a point in the player's frame (X right, F forward, Y up).
  - `sealed class RoadView`: `const double Ahead = 1520, Behind = 320`; `void Build(Track track, double distance)`; `RoadPoint At(double d, double lateral = 0)`.
  - `readonly record struct RoadPose(double Distance, double Lateral, double Heading)`.
  - `static class RoadMotion`: `static RoadPose MoveStraight(Track track, RoadPose pose, double step)`.

Port `RoadView` from the prototype's `Track.cs` (exact heading integration, with the heading interpolated in `At`). `MoveStraight` advances by `along = step·cos h`, adds `step·sin h` to Lateral, subtracts `CurvatureAt(Distance + along/2)·along` from Heading, and adds `along` to Distance. This is the prototype's `World.MoveStraight`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Straight_road_is_straight() { var v = new RoadView(); v.Build(Track.FromSegments(new double[400]), 0); var p = v.At(300, 2); Assert.Equal(2, p.X, 6); Assert.Equal(300, p.F, 6); }
[Fact] public void Constant_bend_matches_circle()   // radius 200 m, bend right
{
    var v = new RoadView(); v.Build(Track.FromSegments(Enumerable.Repeat(1 / 200.0, 400).ToArray()), 0);
    double d = 150, theta = d / 200;
    var p = v.At(d);
    Assert.Equal(200 * (1 - Math.Cos(theta)), p.X, 3);
    Assert.Equal(200 * Math.Sin(theta), p.F, 3);
}
[Fact] public void Bend_curves_same_side_behind() { var v = new RoadView(); v.Build(Track.FromSegments(Enumerable.Repeat(1 / 200.0, 400).ToArray()), 1000); Assert.True(v.At(-100).X > 0); Assert.True(v.At(-100).F < 0); }
[Fact] public void Long_tight_bend_wraps_round()   // 300 m on radius 180 m turns 95°: the road comes back toward the camera plane
{ var v = new RoadView(); v.Build(Track.FromSegments(Enumerable.Repeat(1 / 180.0, 400).ToArray()), 0); Assert.True(v.At(300).F < v.At(280).F); }
[Fact] public void Straight_motion_leaves_a_bend_on_the_outside()
{
    var t = Track.FromSegments(Enumerable.Repeat(1 / 200.0, 400).ToArray());
    var pose = new RoadPose(0, 0, 0);
    for (int i = 0; i < 100; i++) pose = RoadMotion.MoveStraight(t, pose, 1);
    // Chord geometry: after ~100 m a straight line is 200 - sqrt(200² - 100²) ≈ 26.8 m outside (left, negative).
    Assert.InRange(pose.Lateral, -28.5, -25.5);
}
```
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat: exact road view and straight-line motion in road coordinates`.

### Task 4: Feel settings and throttle schemes

**Files:**
- Create: `Control/FeelSettings.cs`, `ControlFrame.cs`, `GameAction.cs`, `PedalOutput.cs`, `IThrottleScheme.cs`, `RampPedalScheme.cs`, `SpeedLeverScheme.cs`
- Test: `tests/NightPursuit.Core.Tests/ThrottleSchemeTests.cs`

**Interfaces:**
- Produces:
  - `sealed class FeelSettings : INotifyPropertyChanged`, with settable doubles and their defaults:
    - throttle and speed: `ThrottleRampUp 1.5`, `ThrottleRampDown 3.0`, `MaxSpeedMph 155`, `MaxAccel 9.0`, `BrakeDecel 14`
    - nose: `TurnRateDeg 90`, `MaxHeadingDeg 20`, `MaxSideSpeed 10`
    - steering: `SteerRampUp 4`, `SteerReturn 7`, `Straighten 8`, `SpeedSensitiveSteering true`, `AssistWhileSteering true` (bool)
    - lever: `LeverRateMph 30`
    - derived: `double MaxSpeed` (m/s), `double TurnRateAt(double v)`, `double MaxHeadingAt(double v)` (copy both from the prototype's `Tuning.cs`), `void Reset()`.
  - `sealed class ControlFrame { int SteerDir; bool Gas, Brake, Fire; bool PadConnected, PadFire; double PadSteer, PadGas, PadBrake; static ControlFrame Empty }`
  - `enum ActionKind { SelectScheme, Cruise, LeverPreset, LeverUp, LeverDown, DropRear, Pause, ToggleHelp, Restart }`, and `readonly record struct GameAction(ActionKind Kind, int Value = 0)`
  - `readonly record struct PedalOutput(double Throttle, double Brake)`
  - `interface IThrottleScheme { string Name { get; } string[] HelpLines { get; } double? TargetSpeed { get; } string? Status { get; } void Reset(double currentSpeed); void Handle(GameAction a, double currentSpeed, FeelSettings f); PedalOutput Update(ControlFrame input, double currentSpeed, FeelSettings f, double dt); }`
  - `RampPedalScheme` (with cruise) and `SpeedLeverScheme`: port from the prototype's `Schemes.cs`, including `HoldSpeed`'s steady-throttle formula `(0.6 + 1.6r) / (A·max(0.05, 1−r²) + 1.6r)` and its brake rule (brake when 1.5 m/s too fast, gain 0.12). The schemes return `PedalOutput` instead of writing to a car.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Ramp_builds_at_ramp_rate() { var s = new RampPedalScheme(); var f = new FeelSettings(); PedalOutput o = default; for (int i = 0; i < 60; i++) o = s.Update(new ControlFrame { Gas = true }, 0, f, 1 / 120.0); Assert.Equal(0.75, o.Throttle, 6); }
[Fact] public void Ramp_falls_away_on_release() { var s = new RampPedalScheme(); var f = new FeelSettings(); for (int i = 0; i < 120; i++) s.Update(new ControlFrame { Gas = true }, 0, f, 1 / 120.0); var o = s.Update(new ControlFrame(), 0, f, 0.1); Assert.Equal(0.7, o.Throttle, 6); }
[Fact] public void Cruise_holds_speed_and_brake_cancels() { var s = new RampPedalScheme(); var f = new FeelSettings(); s.Handle(new GameAction(ActionKind.Cruise), 40, f); Assert.Equal(40, s.TargetSpeed); s.Update(new ControlFrame { Brake = true }, 40, f, 0.01); Assert.Null(s.TargetSpeed); }
[Fact] public void Lever_preset_sets_fraction_of_top_speed() { var s = new SpeedLeverScheme(); var f = new FeelSettings(); s.Handle(new GameAction(ActionKind.LeverPreset, 8), 0, f); Assert.Equal(0.8 * f.MaxSpeed, s.TargetSpeed!.Value, 6); }
[Fact] public void Lever_brakes_when_too_fast() { var s = new SpeedLeverScheme(); var f = new FeelSettings(); s.Handle(new GameAction(ActionKind.LeverPreset, 2), 0, f); var o = s.Update(new ControlFrame(), 60, f, 1 / 120.0); Assert.True(o.Brake > 0); Assert.Equal(0, o.Throttle); }
[Fact] public void Max_heading_shrinks_with_speed() { var f = new FeelSettings(); Assert.Equal(20 * Math.PI / 180, f.MaxHeadingAt(20), 9); Assert.Equal(Math.Asin(10 / 69.3), f.MaxHeadingAt(69.3), 9); }
```
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat: feel settings, ramp pedal with cruise, speed lever`.

### Task 5: Steering input and player car physics

**Files:**
- Create: `Control/SteeringInput.cs`, `Vehicles/VehicleStats.cs`, `Vehicles/Vehicle.cs`, `Vehicles/PlayerCar.cs`, `Vehicles/CarPhysics.cs`
- Test: `tests/NightPursuit.Core.Tests/PlayerPhysicsTests.cs`

**Interfaces:**
- Consumes: `Track`, `FeelSettings`, `PedalOutput`, `ControlFrame`.
- Produces:
  - `sealed class SteeringInput { double Value { get; } bool UsingStick { get; } void Update(ControlFrame input, FeelSettings f, double dt); void Reset(); }`. Keyboard ramps at SteerRampUp, returns at SteerReturn, and reverses at their sum. The stick is used when `PadConnected && SteerDir == 0 && PadSteer != 0`.
  - `enum VehicleState { Driving, Spinning, Crashed, Wrecked }`, `enum Faction { Player, Hostile }`.
  - `sealed record VehicleStats(double Width, double Length, double Height, double TopSpeed, double Accel, double SideSpeed, double Armor, int Points)`, with presets:
    - `Player(1.9, 4.4, 1.3, 0, 0, 0, 100, 0)`
    - `Runner(1.9, 4.4, 1.4, 58, 6, 3.5, 6, 100)`
    - `Blocker(2.4, 6.0, 2.1, 40, 4, 2.8, 14, 300)`
    - `Pursuer(1.9, 4.4, 1.15, 85, 8, 6, 10, 500)`
  - `abstract class Vehicle`:
    - identity: `int Id`, `VehicleStats Stats`, `abstract Faction Faction`
    - motion: `double Distance, Lateral, Heading, Speed, PushVel, LateralVel`
    - condition: `double Armor`, `VehicleState State`, `double StateTimer, SpinAngle, SpinRate, HitFlash, ContactCooldown`
    - `bool IsOnRoadDriving => State is VehicleState.Driving or VehicleState.Spinning`
  - `sealed class PlayerCar : Vehicle`: `double Throttle, Brake, Steer, Shake`, `bool OffRoad`, `int SpareCars = 3`.
  - `static class CarPhysics`: `static void StepPlayer(PlayerCar car, PedalOutput pedals, double steer, Track track, FeelSettings f, double dt)`.

The speed model and turn-to-aim steering are ported exactly from the prototype's `World.Physics`:
- engine `t·A·(1−r²)`, coast `0.6 + 1.6(1−t)r` (only when v > 0.1), brake `b·BrakeDecel`
- gravity `−9.8·slope·0.6`; off road (|Lateral| > 7.9) adds `0.35v + 2`
- turn `steer·TurnRateAt(v) − k·v − Heading·Straighten` (the assist term applies always when `AssistWhileSteering`, otherwise only when |steer| < 0.05); Heading is clamped to ±MaxHeadingAt(v)
- LateralVel `v·sin(Heading) + PushVel`, and PushVel decays by `e^(−5dt)`
- guard-rail at ±12: clamp, halve Heading, speed `·e^(−1.5dt)`, 6 armor per s, Shake ≥ 0.3
- Distance `+= v·cos(Heading)·dt`; off-road Shake ≥ 0.12; Shake decays by 1 per s

- [ ] **Step 1: Write failing tests** (helper `TestWorlds.Straight()` returns `Track.FromSegments(new double[4000])`; `TestWorlds.Bend(r)` returns a constant bend)

```csharp
[Fact] public void Full_throttle_reaches_about_154_mph()   // top speed comes from fall-off and coast, not a hard cap
{ var car = new PlayerCar(); var f = new FeelSettings(); for (int i = 0; i < 120 * 60; i++) CarPhysics.StepPlayer(car, new(1, 0), 0, TestWorlds.Straight(), f, 1 / 120.0); Assert.InRange(Units.MsToMph(car.Speed), 150, 155); }
[Fact] public void Brakes_stop_from_60_mph_in_under_30_m() { /* start 26.8 m/s, Brake 1 → distance travelled < 30 */ }
[Fact] public void Uphill_slows_coasting_car_more_than_flat() { /* height rising 10%: speed after 5 s coasting < flat case */ }
[Fact] public void No_input_on_bend_settles_at_predicted_drift()   // DRV-5: drift ≈ k·v²/Straighten
{ /* bend r=300, hold speed 40 by setting car.Speed each step; after 10 s, LateralVel ≈ -(1/300)·40²/8 = -0.667 ± 0.05 */ }
[Fact] public void Held_key_at_top_speed_slides_no_faster_than_10_mps() { /* v=69, steer=1 for 3 s: max |LateralVel - PushVel| ≤ 10.01 */ }
[Fact] public void Letting_go_after_a_lane_change_stops_the_slide_quickly()   // DRV-4: no overshoot weave
{ /* straight road, v held at 67 m/s, SteeringInput with SteerDir=1 until Lateral ≥ 3, then SteerDir=0: peak Lateral ≤ 4.6 and |LateralVel| < 0.5 within 0.5 s of release */ }
[Fact] public void Off_road_sets_flag_and_shake() { /* Lateral = -9 → OffRoad true, Shake ≥ 0.12, extra slowing */ }
[Fact] public void Guard_rail_clamps_and_damages() { /* Lateral = -13 → Lateral -12, Armor < 100 */ }
[Fact] public void Keyboard_steering_ramps_and_returns() { /* SteeringInput: hold right 0.125 s → 0.5; release 1/14 s → 0 */ }
```
Write each `/* … */` test with the setup and assertion stated in its comment.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat: player car physics with turn-to-aim steering`.

### Task 6: Agent cars and their physics

**Files:**
- Create: `Vehicles/AgentCar.cs`, `Vehicles/DriveIntent.cs`; Modify: `Vehicles/CarPhysics.cs`
- Test: `tests/NightPursuit.Core.Tests/AgentPhysicsTests.cs`

**Interfaces:**
- Produces:
  - `enum AgentKind { Runner, Blocker, Pursuer }`
  - `sealed class AgentCar : Vehicle`: `AgentKind Kind`, `int Side` (−1 or +1), `object? DriverState`, `static AgentCar Create(int id, AgentKind kind, double distance, double lateral, double speed)`. `Create` picks stats by kind and sets Armor = Stats.Armor.
  - `readonly record struct DriveIntent(double TargetSpeed, double TargetLateral, bool Fire = false, bool DropRear = false)`
  - `CarPhysics.StepAgent(Vehicle car, DriveIntent intent, double dt)`, as in the prototype's `Drive`:
    - speed moves toward `min(TargetSpeed, Stats.TopSpeed)` at Accel (up) or 10 (down)
    - steer = `clamp((TargetLateral − Lateral)·1.8, ±Stats.SideSpeed)`, LateralVel = steer + PushVel, PushVel decays `e^(−1.2dt)`
    - Heading = `atan2(LateralVel, max(Speed, 1))` (for drawing)
    - Distance += Speed·dt
  - `CarPhysics.StepSpinning(Vehicle car, double dt)`: SpinAngle += SpinRate·dt, Speed decays `e^(−0.9dt)`, Lateral += PushVel·dt, and back to Driving when StateTimer ≤ 0.
  - `CarPhysics.StepWrecked(Vehicle car, double dt)`: decelerates at 25 m/s².

- [ ] **Step 1: Write failing tests:** the agent reaches its target lateral within 2 s on a straight; the target speed is capped by TopSpeed; a spinning car recovers after StateTimer and keeps sliding sideways.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat: agent cars and their physics`.

### Task 7: World, fixed-step loop and events

**Files:**
- Create: `Simulation/World.cs`, `Simulation/GameEvent.cs`
- Test: `tests/NightPursuit.Core.Tests/WorldLoopTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `enum Tone { Info, Good, Bad }`, `enum KillCause { Gunfire, RanOffRoad, WreckCollision }`.
  - `abstract record GameEvent(double Time)`, with records:
    - `Message(double Time, string Text, Tone Tone)`
    - `VehicleDestroyed(double Time, int VehicleId, AgentKind Kind, KillCause Cause, int Points)`
    - `PlayerDamaged(double Time, double Amount, string Reason)`
    - `VehicleSpun(double Time, int VehicleId, string Reason)`
    - `PlayerWrecked(double Time, string Reason)`
  - `sealed class World`:
    - construction: `World(Track track, FeelSettings feel, IRng rng)`
    - state: `Track Track`, `RoadView View`, `FeelSettings Feel`, `IRng Rng`, `PlayerCar Player`, `List<AgentCar> Agents`, `List<Tracer> Tracers`, `List<Hazard> Hazards`, `IThrottleScheme Scheme`, `SteeringInput Steering`, `double Time`, `int Score`
    - events: `IReadOnlyList<GameEvent> Events` (since the last `Advance`) and `List<GameEvent> RecentMessages` (≤ 4, each kept 2.5 s)
    - stepping: `const double Step = 1.0 / 120`, `int Advance(ControlFrame input, double frameSeconds)`, `void StepOnce(ControlFrame input)`
    - actions and agents: `void Handle(GameAction a)`, `int NextId()`, `void Publish(GameEvent e)`
    - `ulong StateHash()`: FNV-1a over the player and every agent's Distance, Lateral, Speed and Armor, rounded to 1e-6
  - `Advance` keeps an accumulator. It clamps `frameSeconds` to 0.25, runs whole steps, carries the remainder, rebuilds `View` once at the end, and returns the step count.
  - `StepOnce` in this task: scheme → steering → `CarPhysics.StepPlayer`. Later tasks add weapons, agents, collisions and hazards at the marked points (`// T8`, `// T9`, `// T10`).
  - `Handle`: `SelectScheme` 0 = ramp, 1 = lever (calls `Reset(Player.Speed)`); `Cruise`/`LeverPreset`/`LeverUp`/`LeverDown` go to the scheme; `DropRear` is handled in Task 9.

- [ ] **Step 1: Add `TestWorlds.World(int seed = 1, Track? track = null)` (default: `new Track(new TrackProfile(seed))`, new `FeelSettings`, `SeededRng(seed)`), then write failing tests**

```csharp
[Fact] public void Advance_runs_whole_steps_and_carries_remainder() { var w = TestWorlds.World(); Assert.Equal(1, w.Advance(ControlFrame.Empty, 0.01)); Assert.Equal(1, w.Advance(ControlFrame.Empty, 0.01)); /* 0.02 total = 2 steps, remainder 0.0033 */ }
[Fact] public void Long_stall_is_capped_at_quarter_second() { var w = TestWorlds.World(); Assert.Equal(30, w.Advance(new ControlFrame { Gas = true }, 2.0)); }
[Fact] public void Same_seed_same_inputs_same_hash()
{
    ulong Run() { var w = TestWorlds.World(seed: 11); for (int i = 0; i < 7200; i++) w.Advance(new ControlFrame { Gas = true, SteerDir = (i / 240) % 3 - 1 }, 1 / 120.0); return w.StateHash(); }
    Assert.Equal(Run(), Run());
}
[Fact] public void Selecting_lever_resets_target_to_current_speed() { /* drive to 30 m/s, SelectScheme 1 → Scheme.TargetSpeed ≈ 30 */ }
```
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS. (Re-run the determinism test after Tasks 8–11. It must keep passing.)
- [ ] **Step 5: Commit** `feat: world with fixed 120 Hz steps, events and state hash`.

### Task 8: Machine guns, tracers and wrecks from gunfire

**Files:**
- Create: `Weapons/Weapon.cs`, `MachineGuns.cs`, `Loadout.cs`, `Tracer.cs`, `Hazards/Hazard.cs`, `Hazards/Wreck.cs`; Modify: `Simulation/World.cs`
- Test: `tests/NightPursuit.Core.Tests/GunTests.cs`

**Interfaces:**
- Produces:
  - `abstract class Weapon { abstract string Name { get; } abstract void Update(double dt); }`
  - `sealed class MachineGuns : Weapon`:
    - `const double Interval = 0.075, MuzzleSpeed = 260, Range = 380`
    - `double Heat` (0–1): +0.25/s while firing, −0.5/s otherwise
    - `bool Locked`: becomes true when Heat reaches 1, lasts 2 s, then Heat = 0
    - `bool TryFire(double dt)`: true when a shot is due; it alternates guns
    - `bool LeftGun`
  - `sealed class Loadout { MachineGuns Guns; OilDropper Oil; }` (OilDropper comes from Task 9; add it there)
  - `sealed class Tracer { RoadPose Pose; double PrevDistance, PrevLateral, Travel, Speed; int OwnerId; bool Dead; }`
  - `abstract class Hazard { double Distance, Lateral; bool Expired; }`, and `sealed class Wreck : Hazard { double Width, Length; double Burn; }`, with Burn counting up for the flicker effect
  - `World.Destroy(AgentCar car, KillCause cause)`: sets Wrecked; if the cause is Gunfire, adds a `Wreck` at the car's Distance/Lateral with its Width/Length; publishes `VehicleDestroyed`; adds Points to Score
  - In `World.StepOnce` (`// T8`): guns fire from `(Distance + 2cosh − side·sinh, Lateral + 2sinh + side·cosh)` with `side = ±0.7` and Heading = player Heading, and `Speed = player.Speed + MuzzleSpeed`. Tracers move with `RoadMotion.MoveStraight`. The hit test is **swept**: it hits when the car's span `[Distance ± Length/2]` overlaps `[PrevDistance, Distance]`, and the lateral, interpolated at the car's distance, is within `Width/2 + 0.15`. A tracer dies after Range metres of travel or at |Lateral| > 60. Each hit takes 1 armor and sets HitFlash 0.08; armor ≤ 0 calls `Destroy(car, Gunfire)`.
  - Wrecks expire once they are more than 600 m behind the player (WRK-3).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Tracer_on_straight_keeps_its_line() { /* fire with Heading 0 at Lateral 0: after 300 m travel, |Lateral ∓ 0.7| < 1e-6 */ }
[Fact] public void Tracer_on_bend_leaves_road_on_outside() { /* bend r=200 right, Heading 0: after 100 m, Lateral ≈ -26.8 ± 1.5 (matches Task 3 chord) */ }
[Fact] public void Swept_hit_cannot_tunnel() { /* agent 30 m ahead same lane; one tracer, dt = 1/120 at Speed 330: car is hit (Armor 5) */ }
[Fact] public void Guns_overheat_after_4_s_and_lock_2_s() { /* hold Fire: Locked becomes true at 4.0 ± 0.05 s; false again at 6.0 ± 0.05 s */ }
[Fact] public void Gun_kill_leaves_wreck_in_lane() { /* Runner (6 armor) at lane +5 killed by gunfire → Hazards has one Wreck with Lateral 5 and VehicleDestroyed(Gunfire, 100) published; Score 100 */ }
[Fact] public void Wrecks_clear_600_m_behind() { /* wreck at 0, player at 601 → Expired */ }
```
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat: straight-flying tracers, overheating guns, gunfire wrecks`.

### Task 9: Oil, spins, crashes and collisions

**Files:**
- Create: `Weapons/OilDropper.cs`, `Hazards/OilSlick.cs`, `Simulation/Collisions.cs`; Modify: `World.cs`, `Loadout.cs`
- Test: `tests/NightPursuit.Core.Tests/CollisionTests.cs`, `OilTests.cs`

**Interfaces:**
- Produces:
  - `sealed class OilDropper : Weapon`: `int Charges` (starts at the mission's StartingOil, max 5), recharges one charge every 8 s, `bool TryDrop()` with a 0.6 s cooldown.
  - `sealed class OilSlick : Hazard { const double Radius = 2.4; double Life = 25; }`, dropped 5 m behind the player.
  - `World.Spin(Vehicle v, int dir, string reason)`:
    - sets Spinning, StateTimer 1.8, SpinRate `dir·(6 + 3·rng)`
    - sets PushVel `dir·(4 + 3·rng)` and multiplies Speed by 0.8
    - publishes `VehicleSpun`
  - `static class Collisions`, with `static void Resolve(World w)`:
    - **car vs car** (player against each agent): port the prototype's rules — rear-end, rammed from behind, sideswipe, and spin-on-ram when `playerInto > 5` — using each car's own Width/Length.
    - **car vs wreck** (WRK-3), for the player and agents:
      - player hits a wreck: 30 damage, Speed = min(Speed, 5), Shake 0.5, message "HIT A WRECK"
      - an agent hits a wreck: `Destroy(agent, WreckCollision)`, which leaves no new wreck
    - **car vs oil**: a driving agent overlapping a slick (|dDist| < R + Length/2, |dLat| < R + Width/2) spins.
  - Off-road crash (WRK-2): an agent with |Lateral| > 8.5 that is Spinning or has |PushVel| > 2 calls `Destroy(agent, RanOffRoad)`, which leaves no wreck.
  - Player armor ≤ 0 publishes `PlayerWrecked` once.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Oil_in_front_of_pursuer_makes_it_spin() { /* scenario test: pursuer 20 m behind same lane at +10 m/s; DropRear; within 3 s it has State Spinning */ }
[Fact] public void Spinning_car_off_road_crashes_without_wreck() { /* spin with PushVel +7 at lane +5 → Destroy(RanOffRoad); Hazards has no Wreck */ }
[Fact] public void Hard_side_ram_spins_target() { /* agent alongside at +2.0, player LateralVel +6 → agent Spinning, player armor reduced */ }
[Fact] public void Player_hitting_wreck_stops_hard() { /* player 60 m/s, wreck 3 m ahead same lateral → after collision Speed ≤ 5, Armor ≤ 70 */ }
[Fact] public void Agent_hitting_wreck_is_destroyed_without_new_wreck() { /* … */ }
[Fact] public void Oil_recharges_one_charge_per_8_s() { /* charges 3 → 4 after 8 s */ }
```
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat: oil slicks, spins, crashes and collisions with wrecks`.

### Task 10: Enemy drivers with wreck avoidance

**Files:**
- Create: `Drivers/IDriver.cs`, `WorldView.cs`, `DriverHelpers.cs`, `RunnerDriver.cs`, `BlockerDriver.cs`, `PursuerDriver.cs`; Modify: `World.cs`, `AgentCar.cs` (gain an `IDriver Driver`)
- Test: `tests/NightPursuit.Core.Tests/DriverTests.cs`

**Interfaces:**
- Produces:
  - `interface IDriver { DriveIntent Decide(AgentCar self, WorldView view, double dt); }`
  - `sealed class WorldView`, a read-only facade: `PlayerCar Player`, `IReadOnlyList<AgentCar> Agents`, `IReadOnlyList<Hazard> Hazards`, `IRng Rng`
  - `static class DriverHelpers`: `static double AvoidWrecks(AgentCar self, WorldView view, double desiredLateral, double lookAhead = 120)`. It returns the nearest lane centre clear of any wreck within lookAhead ahead of the car; with no clear lane it returns desiredLateral.
  - Drivers port the prototype's behaviour and numbers:
    - **Runner:** changes lane every 2.5–6.5 s, and changes lane at once when HitFlash > 0.
    - **Blocker:** within 140 m ahead of the player, its target lateral is the player's Lateral clamped to ±5.5; within 30 m it brake-checks at `max(15, min(cruise, player.Speed − 5))`.
    - **Pursuer:** Approach → Attack → Retreat. Approach: speed = player + 14 (≤ 85), side offset 2.7 m. Attack: 3.5 s, ramming in for the last 2.4 s. Retreat: player − 12 until 80 m behind.
    - Every driver passes its lateral through `AvoidWrecks` with a reaction delay (Runner 0.5 s, Blocker 0.8 s, Pursuer 0.3 s), using the `DriverState` timer.
  - `World.StepOnce` (`// T10`): each Driving agent calls `Driver.Decide` → `StepAgent`; spinning agents call `StepSpinning`; wrecked ones call `StepWrecked`. Removal: Wrecked after 1.5 s, or |dDistance| > 1500 ahead or > 500 behind.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Blocker_moves_into_player_lane() { /* blocker 80 m ahead at lane -5, player at +5 → within 4 s |Lateral - 5| < 0.6 */ }
[Fact] public void Pursuer_cycles_through_phases() { /* starts 200 m behind; reaches Attack within 30 s; then Retreat */ }
[Fact] public void Destroying_a_car_ahead_of_a_pursuer_blocks_it() { /* scenario (WRK-4 tactic): wreck placed in pursuer's lane 60 m ahead of it → pursuer changes lane after its 0.3 s reaction and is not destroyed */ }
[Fact] public void Avoid_wrecks_picks_nearest_clear_lane() { /* wrecks in lanes 0 and +5 → AvoidWrecks from 0 returns -5 */ }
```
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS, including Task 7's determinism test.
- [ ] **Step 5: Commit** `feat: runner, blocker and pursuer drivers with wreck avoidance`.

### Task 11: Mission definition, loader, director and run

**Files:**
- Create: `Missions/MissionDefinition.cs`, `MissionLoader.cs`, `MissionDirector.cs`, `MissionRun.cs`, `content/missions/mission-01.json`
- Test: `tests/NightPursuit.Core.Tests/MissionTests.cs` (the Tests project copies `content/**` to its output, as ASLCoach does)

**Interfaces:**
- Produces:
  - `sealed record MissionDefinition(string Id, string Title, string Briefing, TrackProfile Road, double ApproachLength, int StartingOil, IReadOnlyList<WaveDefinition> Waves)`
  - `sealed record WaveDefinition(double AtDistance, AgentKind Kind, int Count, double SpawnOffset)`: a positive offset spawns ahead of the player, a negative one behind. Cars in a wave spread 40 m apart, in lanes chosen with `IRng`.
  - `static class MissionLoader`:
    - `static MissionDefinition Load(string json, string sourceName)`
    - `static MissionDefinition LoadFile(string path)`
    - JSON is camelCase, and enums are strings.
    - Throws `MissionFormatException(string message)` for bad input. The message names the source and the field, e.g. `"mission-01.json: waves[2].kind 'Tank' is not a known enemy type"`.
    - Validation rules: `approachLength` > 0, waves in ascending `atDistance`, `count` 1–10, `startingOil` 0–5.
  - `sealed class MissionDirector`: `MissionDirector(MissionDefinition def)`, `void Update(World w)` (spawns each wave once when the player passes AtDistance), `bool Completed`, `double DistanceRemaining(World w)`.
  - `enum RunState { Playing, Paused, Respawning, Completed, GameOver }`
  - `sealed class MissionRun`:
    - `MissionRun(MissionDefinition def, FeelSettings feel, int seed)`
    - `World World`, `MissionDirector Director`, `RunState State`, `double StateTimer`
    - `void Update(ControlFrame input, double frameSeconds)`, `void Handle(GameAction a)` (Pause toggles; Restart rebuilds the run)
    - On `PlayerWrecked`: Respawning for 3 s, then SpareCars−1, Armor 100, Speed 0, Lateral 0, Heading 0, and agents within 200 m removed. With no spares left: GameOver.
    - When the Director completes: Completed. (The boss comes in Plan 2.)
  - `mission-01.json`: Road seed 7, approachLength 6000, startingOil 5, six waves over Runner, Blocker and Pursuer.

- [ ] **Step 1: Write failing tests:**
  - `mission-01.json` loads;
  - an unknown kind gives a message containing `waves[`;
  - waves out of order are rejected;
  - a wave spawns exactly once;
  - completing approachLength sets Completed;
  - the fourth wreck (no spares left) sets GameOver;
  - the respawn removes agents within 200 m.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: mission definitions, director and mission run`.

### Task 12: App shell, game loop and input

**Files:**
- Create: `src/NightPursuit.App/NightPursuit.App.csproj` (references Core; copies `content/**` to output), `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, `GameLoop.cs`, `Input/KeyboardReader.cs`, `Input/XInputPad.cs`, `Input/InputHub.cs`, `Rendering/GameView.cs` (stub that draws black plus the speed text)

**Interfaces:**
- Consumes: `MissionRun`, `ControlFrame`, `GameAction`.
- Produces:
  - `sealed class InputHub`:
    - `bool OnKeyDown(Key key, bool isRepeat)`, `void OnWheel(int delta)`
    - `IEnumerable<GameAction> Drain()`, `ControlFrame Read(bool windowActive)`
    - `bool PadConnected`
  - Key map:
    - drive: arrows/WASD; Space guns; X or Ctrl DropRear
    - controls: C Cruise; 1–9, 0 LeverPreset; F2/F3 SelectScheme 0/1; mouse wheel LeverUp/LeverDown
    - other: P/Esc Pause; F1 ToggleHelp; R Restart
  - Gamepad map: RT/LT pedals, left stick, A guns, B DropRear, Y Cruise, D-pad LeverUp/LeverDown, Start Pause. Port `XInputPad` from the prototype; it polls an absent pad once a second and survives a missing DLL.
  - `sealed class GameLoop`: hooks `CompositionTarget.Rendering`, skips duplicate `RenderingTime` values, and calls `run.Update(input.Read(window.IsActive), seconds)`, then `view.InvalidateVisual()`.
  - Startup: loads `content/missions/mission-01.json` with `MissionLoader.LoadFile`. A `MissionFormatException` or missing file shows a message window with the exception text and exits cleanly (Review Focus 4).

- [ ] **Step 1: Build** with `dotnet build src/NightPursuit.App -c Release`. Expected: succeeded.
- [ ] **Step 2: Manual check (owner):** launch, hold W, and the speed text rises. Hold A, alt-tab away and back: the car is not still steering (Review Focus 2). Rename the mission file, launch, and a readable error appears. Restore the file.
- [ ] **Step 3: Commit** `feat: app shell, input and game loop`.

### Task 13: Projector, sky and road rendering, snapshot diagnostic

**Files:**
- Create: `Rendering/Neon.cs` (palette and `NeonPen`), `LineBatch.cs`, `Projector.cs`, `SkyRenderer.cs`, `RoadRenderer.cs`, `Diagnostics/DiagnosticsRunner.cs`, `Diagnostics/Autopilot.cs`; Modify: `GameView.cs`, `App.xaml.cs`

**Interfaces:**
- Produces:
  - `sealed class Projector`:
    - `void Begin(double width, double height, World w)`: 70° FOV (`Focal = 1/tan 35°`), horizon at 0.44·height, eye 1.15 m
    - `bool ToScreen(double d, double lateral, double up, out Point pt, out double depth)`: rotates by the player's Heading, and is false when depth < 0.8
    - `double Ppm(double depth)`, `double ClipAt(double d)`, `void SetClip(int i, double y)`, `double PxPerRadian`
  - `RoadRenderer.Draw(DrawingContext dc, Projector p, World w)`: port the prototype's `DrawRoad` with both crash fixes. The hiding line is updated only by road heading away (`depth ≥ 0.3·d`) and is clamped ≥ 0; `LineBatch` skips points beyond ±50 000 px.
  - `SkyRenderer.Draw(...)`: stars, moon and ridge, shifted by track heading + player Heading.
  - `static class Autopilot`: `static ControlFrame Next(World w)`, ported from the prototype's snapshot autopilot.
  - `DiagnosticsRunner.Run(string[] args) → int`: handles `--snapshot out.png [--seconds S] [--scene name]`, simulating with `Autopilot` at 1/120 s, then rendering `GameView` at 1280×720 through `RenderTargetBitmap`. `App.OnStartup` calls it for any `--snapshot`, `--stress` or `--perf` argument, then `Shutdown(exitCode)`.

- [ ] **Step 1: Implement and build.** Expected: succeeded.
- [ ] **Step 2: Run** `NightPursuit.App.exe --snapshot snapshots/t13.png --seconds 20`. Read the PNG. Expected: road edges, lane dashes, posts, lamps, ridge, stars and moon. Road beyond hills is hidden, and long bends wrap round correctly.
- [ ] **Step 3: Commit** `feat: projector, road and sky rendering, snapshot diagnostic`.

### Task 14: Vehicles, hazards and effects rendering

**Files:**
- Create: `Rendering/VehicleRenderer.cs`, `HazardRenderer.cs`, `EffectRenderer.cs`; Modify: `GameView.cs`, `DiagnosticsRunner.cs` (adds `--scene close-cars`)

**Interfaces:**
- Produces:
  - `VehicleRenderer.DrawRear(DrawingContext dc, Projector p, AgentCar car, World w)`:
    - draws each kind with its own outline and colour: Runner magenta sedan, Blocker amber box truck with door line, Pursuer red low car with spoiler
    - a filled black body hides the road behind it; there are tail lights
    - a spinning car tilts ±25° and narrows; a health bar shows when damaged and within 220 m
    - the car is clipped by `ClipAt`, and a car whose clip is ≤ 0 is skipped
  - `HazardRenderer`: oil slicks are purple ellipses. A wreck is a crumpled outline with orange flickering flames driven by `Burn`.
  - `EffectRenderer`: tracers are 7 m amber streaks along each tracer's own heading; explosions are an 18-ray burst over 1.2 s; muzzle flash.
  - `World` publishes positions only. Explosions are spawned by the App from `VehicleDestroyed` events into an App-side `EffectList`, because effects are presentation.

- [ ] **Step 1: Implement and build.**
- [ ] **Step 2: Run** `--snapshot snapshots/t14.png --seconds 6 --scene close-cars`. The scene places a Runner 22 m ahead, a Blocker 70 m ahead, a spinning Pursuer 140 m ahead, a wreck 50 m ahead, an oil slick 40 m ahead and a Pursuer 25 m behind. Read the PNG. Expected: all are visible, the wreck is burning, and nothing is drawn through the car bodies.
- [ ] **Step 3: Commit** `feat: vehicle, hazard and effect rendering`.

### Task 15: HUD, mirror, radar and warnings

**Files:**
- Create: `Rendering/Hud.cs`, `Mirror.cs`, `Radar.cs`; Modify: `GameView.cs`

**Interfaces:**
- Produces:
  - **Driving readouts (HUD-1):** big mph, throttle/brake bars, target-speed marker, steering bar and "NOSE n° LEFT/RIGHT", aim mark at screen centre.
  - **Car readouts:** armor bar, spare cars as car icons, gun heat gauge (red and "GUNS LOCKED" when locked), oil charges as pips, score, "DISTANCE TO GO n.n km".
  - **Mirror (HUD-2):** port it with the camera rotation.
  - **Radar (HUD-3):** colour per kind plus shape, wrecks as X, slicks as circles, player arrow rotated by Heading.
  - **Warnings (HUD-4):** "PURSUER BEHIND LEFT/RIGHT n m" with an edge arrow; "BLOCKER AHEAD"; "WRECK AHEAD" when a wreck is in the player's lane within 150 m; "<< OFF ROAD" / "OFF ROAD >>"; red edge flash on damage.
  - **Messages and overlays:** `RecentMessages`, coloured by Tone. Pause overlay, help overlay (F1, shown at start until gas), "RESPAWNING", "MISSION COMPLETE  (R to drive again)", "GAME OVER  (R to restart)". FPS readout in Debug builds only.

- [ ] **Step 1: Implement and build.**
- [ ] **Step 2: Run snapshots** `--scene close-cars` and `--scene offroad`, and read both. Expected: every element listed above is present and readable at 1280×720, and the OFF ROAD banner points the right way.
- [ ] **Step 3: Commit** `feat: HUD, mirror, radar and on-screen warnings`.

### Task 16: Stress and performance diagnostics, debug tuning panel

**Files:**
- Create: `Debug/TuningPanel.cs` (inside `#if DEBUG`); Modify: `DiagnosticsRunner.cs`, `MainWindow.xaml(.cs)`

**Interfaces:**
- Produces:
  - **`--stress report.txt`:** renders every 10 m of mission 1's road with 13 agents, 13 slicks, 13 wrecks, tracers and explosions placed at −200…+1490 m. The player's Lateral sweeps ±6 and Heading ±20°. It renders at **960×540, 320×200 and 2560×600**, and 0×0 must return without drawing (Review Focus 3). The report gives frames and failures, with exit code 1 on any failure.
  - **`--perf report.txt`:** 600 frames of the close-cars scene plus 25 agents at 1280×720, reporting mean and 95th-percentile `OnRender` milliseconds. This measures draw-call cost only; actual on-screen frame rate is checked live (NFR-1).
  - **F12 tuning panel (Debug only):** sliders for every `FeelSettings` value, as in the prototype.

- [ ] **Step 1: Implement and build** both Debug and Release.
- [ ] **Step 2: Run** `--stress snapshots/stress.txt`. Expected: `0 failures` at every size.
- [ ] **Step 3: Run** `--perf snapshots/perf.txt`. Expected: mean `OnRender` < 8 ms (half the 16.7 ms frame budget). If it's slower, profile and batch more lines before continuing.
- [ ] **Step 4: Run the whole Core test suite.** Expected: all pass.
- [ ] **Step 5: Manual check (owner):** play mission 1 to the end. Use the guns on a bend, check that a gunfire wreck blocks a lane, oil a pursuer, and ram a runner off the road.
- [ ] **Step 6: Commit** `feat: stress and perf diagnostics, debug tuning panel`.

---

## Spec coverage for this plan

| Spec | Where |
|---|---|
| DRV-1, 2 | Tasks 2, 3, 13 |
| DRV-3 (ramp + cruise, lever, pad triggers) | Tasks 4, 12 |
| DRV-4, 4a, 5, 6 | Tasks 5, 13, 15 |
| DRV-7 (tuning panel in Debug only) | Task 16 |
| WPN-1 (straight tracers, overheat) | Task 8 |
| WPN-2 (oil) | Task 9 |
| WPN-7 (ramming) | Task 9 |
| WRK-1, 2, 3 | Tasks 8, 9 |
| WRK-4 | Task 10 |
| ENM-1, 2 (Runner, Blocker, Pursuer) | Tasks 6, 10, 14 |
| MSN-2 (approach only), MSN-3, MSN-4 (subset) | Task 11 |
| HUD-1…4 (no heat or boss yet) | Task 15 |
| NFR-1 (measured), NFR-2, NFR-3, NFR-5 | Tasks 7, 15, 16 |
| §8.4 diagnostics | Tasks 13, 16 |

**Deferred:**
- Plan 2: WPN-3…6 and 8, the remaining enemy types, the boss, ENM-3/4.
- Plan 3: CIV-*, HT-*.
- Plan 4: MSN-1, 5, HUD-5, NFR-4, and the Settings screen choosing the throttle scheme.
