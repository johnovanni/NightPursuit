# Night Pursuit Plan 2: Arsenal, Enemies and Bosses. Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the full arsenal (smoke, caltrops, missile, satellite strike, supply crates and per-mission
loadouts), the Gunner, Dropper and Armored enemies, a boss with phases at the end of every mission (with
enemy missiles for the phases that use them), and the HUD that shows all of it on screen.

**Architecture:** Core first. New weapons sit under `Weapons/` on a shared `ChargedWeapon` base. New hazards go
under `Hazards/` and new drivers under `Drivers/`. `BossCar : AgentCar` reuses the existing drivers through a
`BossDriver` that swaps behaviour per phase (with boss versions of the Pursuer and Runner behaviours, so a boss is
always within reach of forward guns). `World` becomes a `partial` class: arsenal, enemy fire and boss rules live in
their own files. Warnings and announcements are worked out in Core (`Simulation/Warnings.cs`), so tests can prove
every warning reaches the screen. The App only draws them. It gets new renderers, a weapon panel, a boss bar and new
`--scene` snapshots.

**Tech Stack:** C# / .NET 10, WPF (`net10.0-windows`), xUnit 2.9.3, System.Text.Json, XInput via P/Invoke.

**Spec:** `docs/superpowers/specs/2026-11-17-night-pursuit-design.md`. Also read `docs/decisions.md`: D4 (gunfire
wrecks slide), D5 (wreck damage once, then crab round), D9 (rammed = no wreck), D12 and D13 (focus pause; restart
only when paused or finished), D15 (mission rules run per step), D18 (draw at the interpolated moment through the
Projector), D21 (the retreat lane, focus pause and frame cap as built).

**The four plans:**
1. Foundation and playable slice (built, branch `plan-1-foundation`)
2. **Full arsenal, all six enemy types and the boss** (this plan)
3. Civilians, police, heat and busted (CIV, HT)
4. Six missions, all screens, settings, high scores, persistence (MSN, HUD-5, NFR-4)

## Global Constraints

- Target frameworks: Core and Tests `net10.0`; App `net10.0-windows` with `<UseWPF>true</UseWPF>`. `Directory.Build.props` is unchanged.
- Core references no WPF types and never calls `DateTime.Now`, `Stopwatch`, or `new Random()`. Time arrives as `dt`, and randomness comes from `World.Rng` / `WorldView.Rng`.
- Units inside Core: metres, seconds, m/s, radians. Only the HUD shows mph (`Units.MphPerMs = 2.23694`).
- Simulation step is exactly `World.Step = 1.0 / 120` s. Rules that decide the mission run once per step (D15).
- Everything that moves gets `Prev…` fields, refreshed in `World.CapturePrev`, and is drawn through `Projector.Lerp` (D18). Anything created between steps (by `Handle`) or during a step sets its `Prev…` fields to its spawn values, so it is never drawn sweeping in. The view, the mirror and the radar all take positions from the Projector, never from raw `Distance`.
- **Nothing important may depend on sound.** Every weapon event, warning, crate pickup and boss phase change shows text on screen. Colour is never the only cue: each enemy type also has its own outline and radar shape, and every warning has words. There is no audio in this plan.
- Original work only: no names, cars, art or music from Spy Hunter, Speed Freak or any other game.
- No screen-capture scripts. Visual checks use the app's `--snapshot`, `--stress` and `--perf` options only. Launch the freshly built exe as few times as possible (antivirus): one `--scene a,b,c` run per task.
- App exe: `src/NightPursuit.App/bin/Release/net10.0-windows/NightPursuit.App.exe`, built with `dotnet build src/NightPursuit.App -c Release`. Snapshots go in `snapshots/` (git-ignored).
- **Commits:** local commits on a new branch `plan-2-arsenal-and-bosses` created from `plan-1-foundation`. Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never push.
- Test command (from repo root): `dotnet test tests/NightPursuit.Core.Tests`. Every existing test must keep passing, including the determinism and frame-split tests.
- Another change set landed on `plan-1-foundation` while this plan was written: the PursuerDriver retreat lane, the GameLoop focus pause, the Overlays warning text and the `World.Advance` accumulator clamp (all logged as D21). `World.Advance` is now `Advance(ControlFrame input, double frameSeconds, Func<bool>? afterStep = null)`. Work from the code as it is when you start, not from line numbers quoted here.

## Review Focus

1. **The boss gets lost.** It falls more than 500 m behind, a respawn clears everything within 200 m, or a ram, oil or wreck pushes it past the road edge. Then the mission can never be finished. Expected: the boss leaves only by being destroyed, and it comes back into the fight by itself. *(Task 10: `Boss_is_never_removed_for_distance_and_catches_up`, `Boss_never_runs_off_the_road`; Task 12: `Respawn_puts_the_boss_300_m_ahead_with_its_health_and_phase`.)*
2. **Several blows land in one step.** A strike and a missile arrive together, or a strike hits at 67% health. Expected: health stops at the current phase's threshold, a phase is never skipped, and the fight never ends in one blow. *(Task 10: `One_blow_never_skips_a_phase`; Task 11: `Strike_and_missile_in_one_step_never_skip_a_phase`.)*
3. **An enemy missile gives too little warning.** It is launched close, while you brake hard, or from behind. Expected: the banner is on screen at least 2 s before impact, every time. *(Task 13: `Every_missile_is_announced_at_least_2_s_before_it_arrives`; Task 14: `Missile_warning_shows_every_step_from_launch_to_outcome`.)*
4. **Dropper hazards at top speed.** Oil or caltrops land where a 150 mph player has no time to react. Expected: a drop only happens where your front bumper is at least 1.3 s (and at least 60 m) from the hazard's near edge, after 0.5 s of visible arming. *(Task 9: `Dropper_only_drops_where_the_player_has_1_3_s_to_react`.)*
5. **Weapon keys at the wrong moment:** while wrecked, respawning or paused, with none left, during a cooldown, during a strike sweep, or with nothing to lock onto. Expected: nothing is used up and nothing breaks, and a message says why when the game is running. *(Task 1: `Wrecked_player_drops_nothing`, `Rear_weapon_in_cooldown_says_not_ready`; Task 5: `Fire_missile_with_none_left_or_cooling_says_so`; Task 6: `Second_press_during_sweep_does_nothing`; Task 12: `Weapon_keys_do_nothing_while_paused_or_respawning`.)*

## Calls made in this plan

The spec leaves these open. Each one is logged as a decision for the owner in Task 19.

1. **Keys:** Z or Shift picks the next rear weapon, X or Ctrl drops it, F fires a missile, V calls the satellite strike (spec §8.3). On the pad: X next rear, B drop, RB missile, LB strike. The left hand does everything, and the arrows stay under the right.
2. **Only oil recharges** (8 s, as built). Smoke (3 max), caltrops (3), missiles (2) and the strike (1) come only from the loadout and crates. The spec gives recharging to oil alone, and this gives crates a purpose.
3. **Cooldowns:** oil 0.6 s (as built), smoke 1.0 s, caltrops 0.8 s, missile 1.0 s. A key bounce can't empty a weapon.
4. **Smoke cloud:** 30 m long and 12 m wide, dropped 6 m behind you. It drifts forward at half your speed, slowing at 10 m/s², and lasts 5 s. A small fixed puff would be crossed by a pursuer in a tenth of a second.
5. **Smoke effect:** inside the cloud and for 2 s after, an AI car drives at 60% of its cruise speed (at least 15 m/s), heads for a random point within ±6 m every 0.8 s, and can't aim or fire. That is "loses sight, wanders, slows".
6. **Every pair of cars can collide** (spec §7.2; open item from Plan 1). A rear-end slows the follower. A sideswipe closing faster than 4 m/s spins both cars. So that this doesn't happen all the time in ordinary traffic, every AI car that can see keeps clear of a car ahead in its lane: within 25 m and closing, it moves to a clear neighbouring lane, or else matches that car's speed, after its usual reaction delay. Blinded cars can't, which is how cars "collide inside" smoke.
7. **Caltrops** cover one lane (±2.5 m) for 3 m and last 30 s. A punctured car slows at 5 m/s², wobbles 0.8 m, limps to its nearer road edge and crashes beside the road. That follows WRK-2: no wreck, and it still scores.
8. **Punctured is a flag on `AgentCar`, not a `VehicleState`.** A punctured car can still spin, be shot or be rammed.
9. **Enemy oil and caltrops affect you; your own never do.** Enemy oil: 1 s with no steering grip, a 3 m/s shove and −10% speed. Enemy caltrops: −10 armor, then for 5 s a gentle automatic brake slows you at 4.5 m/s² whenever you are above 100 mph (so 150 → 100 mph takes about 5 s), the throttle is cut at the cap, and the steering wobbles slightly. Your own brake still works; below 100 mph it is just a speed cap. Both are visible in advance, and neither can kill you by itself.
10. **Missile:** it launches at your speed +110 m/s and flies 700 m. It locks onto the nearest hostile 15–500 m ahead within ±25° of the nose (the 70° view less a margin) and turns at 1.5 rad/s. It destroys any non-boss hostile, leaving a wreck. With no lock it flies straight, and the message says so. A missile that flies out of range without a hit shows "MISSILE LOST".
11. **Satellite strike:** with no hostile in range it refuses ("NO TARGETS IN STRIKE RANGE") and keeps the charge, because it is very rare. Once started, it lands even if you are wrecked during the 1.5 s sweep.
12. **Supply crates** are 1.6 m glowing boxes placed by the mission file. Each one appears on the road 800 m before you reach it. Only you collect crates; AI cars and bullets pass through them. Driving into a crate for a weapon that is already full still uses the crate up, and the message says "ALREADY FULL". An extra item, Armor, repairs 25 per amount.
13. **Loadout JSON:** `"loadout": { "oil", "smoke", "caltrops", "missiles", "strikes" }` with strict keys. The old `"startingOil"` is still accepted when it is used alone.
14. **Gunner:** an orange-red (#FF6A3D) fastback with a square gun port in its rear window. It holds 35–55 m ahead of you and follows your lane. It aims for 0.6 s (the port glows and "GUNNER AIMING" shows) and stops following you while it aims, then fires a 6-shot burst every 2.5 s. Each hit costs 2 armor. Its bullets fly back at 160 m/s relative, reach 120 m, and never hit other cars.
15. **Dropper:** a teal (#2EE6C8) van with a tall hatch and a chute. It holds ahead of you in your lane. It drops only where you have at least 1.3 s to react, measured from your front bumper to the hazard's near edge (at least the larger of 60 m and 1.3 s × your speed), after the chute glows for 0.5 s. It alternates oil and caltrops every 3.5 s.
16. **Armored** (the spec gives toughness only): a steel-blue (#6F9BFF) wedge with armour plates. Ahead of you it lumbers at cruise speed. Once you pass it, it chases, rear-ends you, backs off for 2 s, and repeats. Your guns point forward, so the answers are a hazard, a ram, or braking to let it pass and then a missile. Bullets do 0.15 each (12 armor, so 80 hits), and the first hit says how to beat it. It resists rams: a side ram spins it only when you hit it at more than 8 m/s sideways (more than 5 for other cars), so ramming it off the road takes commitment.
17. **Stats and points:** Gunner 9 armor / 600 points, Dropper 9 / 600, Armored 12 / 900. Boss health comes from the mission file (mission 1: 180), and the boss is worth 5000.
18. **Radar shapes:** Gunner an orange-red ring with a dot, Dropper a teal down-triangle, Armored a steel-blue hexagon, Boss a white double ring with a gold (#FFD24A) inner ring. Crates are white boxes with the item's letter.
19. **Boss damage limits:** one hit can at most end the current phase (health stops at that phase's threshold). After a phase change the boss is shielded for 1.5 s, shown on its bar. Gunfire does double damage while it spins (ENM-3) and a quarter in an Armored phase. Ramming does no damage.
20. **Smoke works on the boss as on any car.** ENM-3 doesn't list smoke.
21. **Boss arrival (MSN-2):** at the end of the approach the boss appears 300 m ahead in the centre lane, with a 3 s "BOSS: NAME" card. It eases along at your speed −10 m/s (so it closes from 300 m to 140 m in about 16 s) until you are within 140 m or you first hit it with anything; only then does its phase behaviour start. Wave cars still on the road stay; no new waves spawn. The boss is never removed for distance: once engaged it closes from behind at +25 m/s, or waits for you ahead, so it can't be lost. A respawn puts it 300 m ahead again, easing in at −10 m/s the same way, with its health and phase unchanged.
22. **Enemy missile:** it always closes at 25 m/s and is launched only 60–160 m away, which gives at least 2.1 s of warning ((60 − 2.8 − 2.7) / 25, launched from the boss's near end). It follows your lane at 6 m/s until 1 s before impact, then locks. It hits within a 1.4 m half-width for 25 damage. Only one is in flight at a time, at most one every 6 s. Shooting one down scores +50.
23. **Phase banners** show "PHASE n OF m: BEHAVIOUR" for 2.5 s with a one-line hint; a Pursuer-style boss phase says "LET IT PASS, THEN SHOOT". Warnings and announcements are worked out in Core, so tests prove each one is on screen.
24. **A test mission, `content/missions/proving-ground.json`**, has all six enemy types, every crate item and a 3-phase boss with missiles. It opens with `--mission proving-ground`. The six real missions are Plan 4, but the owner needs somewhere to try all of this now.
25. **Mission 1's boss** is the "Black Heron": 180 health. Phase 1 Blocker until 60% (72 hits), phase 2 Pursuer (it pulls ahead after each attack, call 27) until 25%, phase 3 Runner, weaving fast (call 28), to 0. It fires no missiles (ENM-4).
26. **Repeated hits** (gunner bullets) show their message at most once per second. The red edge flash still shows every hit.
27. **A boss in a Pursuer phase pulls ahead after each attack** instead of dropping back. It comes alongside and rams as a pursuer does, then overtakes in a lane away from you, pulls at least 60 m ahead and holds about 70 m for 2.5 s, then drops back toward you at 8 m/s for the next attack. Your guns point forward, so each cycle gives about 10 s with the boss in front of you. Ordinary Pursuers keep their Plan 1 behaviour.
28. **A boss in a Runner phase weaves:** it hops lanes every 1.0–1.8 s (and at once when shot) and holds about 70 m ahead of you, so it stays a moving target instead of being passed.
29. **Any hostile closing from behind is warned** (HUD-4): within 60 m and closing faster than 3 m/s, "{KIND} BEHIND LEFT/RIGHT n m" with an edge arrow. The Plan 1 pursuer warning and the boss warning (within 120 m) stay as they are.
30. **Each warning also publishes a `WarningRaised` event** on the step it first appears (§7.6), so later sound can follow the screen. `Warnings.Collect` stays the source of truth for what is shown.

---

## File structure

```
content/missions/mission-01.json              (modified: loadout, crates, boss)
content/missions/proving-ground.json          (new)
src/NightPursuit.Core/
  Weapons/   ChargedWeapon.cs, SmokeLauncher.cs, CaltropDropper.cs, MissileLauncher.cs, SatelliteStrike.cs,
             WeaponKinds.cs (RearWeapon, WeaponKind, SupplyItem), LoadoutDefinition.cs, Missile.cs,
             EnemyMissile.cs, EnemyMissileRack.cs, AgentArms.cs
             (modified: Loadout.cs, OilDropper.cs, Tracer.cs)
  Hazards/   SmokeCloud.cs, Caltrops.cs, SupplyCrate.cs            (modified: Hazard.cs, OilSlick.cs)
  Drivers/   BlindDriver.cs, GunnerDriver.cs, DropperDriver.cs, ArmoredDriver.cs, BossDriver.cs,
             BossPursuitDriver.cs   (modified: WorldView.cs, DriverHelpers.cs, RunnerDriver.cs)
  Vehicles/  BossCar.cs   (modified: AgentCar.cs, PlayerCar.cs, VehicleStats.cs, DriveIntent.cs)
  Simulation/ World.Arsenal.cs, World.EnemyFire.cs, World.Boss.cs, BossDamage.cs, HazardContacts.cs,
             Warnings.cs, Announcement.cs   (modified: World.cs → partial, Collisions.cs, GameEvent.cs)
  Missions/  BossDefinition.cs, CrateDefinition.cs
             (modified: MissionDefinition.cs, MissionLoader.cs, MissionDirector.cs, MissionRun.cs)
  Control/   (modified: GameAction.cs)
src/NightPursuit.App/
  Rendering/ ArsenalRenderer.cs, WeaponPanel.cs, BossBar.cs
             (modified: Neon.cs, Projector.cs, VehicleRenderer.cs, Mirror.cs, Radar.cs, EffectList.cs,
              EffectRenderer.cs, HazardRenderer.cs, Hud.cs, Overlays.cs, GameView.cs)
  Input/     (modified: InputHub.cs, XInputPad.cs)
  Diagnostics/ (modified: DiagnosticsRunner.cs)    App.xaml.cs (modified: --mission)
tests/NightPursuit.Core.Tests/
  RearWeaponTests.cs, SmokeTests.cs, CaltropTests.cs, MissileTests.cs, StrikeTests.cs, SupplyTests.cs,
  GunnerTests.cs, DropperArmoredTests.cs, BossTests.cs, BossDamageTests.cs, BossMissionTests.cs,
  EnemyMissileTests.cs, WarningTests.cs
  (modified: CollisionTests.cs, DriverTests.cs, MissionTests.cs, InterpolationTests.cs, TestWorlds.cs)
```

---

### Task 1: Rear weapons, loadout and the new actions

**Files:**
- Create: `Weapons/ChargedWeapon.cs`, `SmokeLauncher.cs`, `CaltropDropper.cs`, `MissileLauncher.cs`, `SatelliteStrike.cs`, `WeaponKinds.cs`, `LoadoutDefinition.cs`, `Hazards/SmokeCloud.cs`, `Hazards/Caltrops.cs`, `Simulation/World.Arsenal.cs`
- Modify: `Weapons/OilDropper.cs`, `Weapons/Loadout.cs`, `Hazards/Hazard.cs`, `Control/GameAction.cs`, `Simulation/World.cs` (make it `partial`; `DropOil` → `DropRear`), `Simulation/GameEvent.cs`
- Test: `tests/NightPursuit.Core.Tests/RearWeaponTests.cs`

**Interfaces:**
- Consumes: `Weapon { string Name; void Update(double dt) }`, `World.Handle(GameAction)`, `World.Publish`, `Message(Time, Text, Tone)`, `Track.LaneCentres`.
- Produces:
  - `abstract class ChargedWeapon : Weapon`:
    - `protected ChargedWeapon(int capacity, int startingCharges, double cooldown, double rechargeTime = 0)`, where `rechargeTime` 0 means it never recharges
    - `int Capacity`, `int Charges`, `double Cooldown`, `double CooldownLeft`, `double RechargeTime`, `double RechargeProgress` (0..1; 0 when full or never recharging), `bool Ready => CooldownLeft <= 0 && Charges > 0`
    - `bool TryUse()`, `int Restock(int amount)` (returns the charges actually added), `override void Update(double dt)`
    - starting charges are clamped to `0..capacity`
  - `OilDropper : ChargedWeapon`:
    - keeps `const int MaxCharges = 5`, `const double DropCooldown = 0.6`, `const double RechargeSeconds = 8`, ctor `(int startingCharges = MaxCharges)`, `bool TryDrop() => TryUse()`, Name "Oil slick"
    - keeps the recharge epsilon from the existing `Update`
    - every existing oil test still passes
  - The other charged weapons:

    | Class | Name | `MaxCharges` | `UseCooldown` | Default starting charges |
    |---|---|---|---|---|
    | `SmokeLauncher` | "Smoke screen" | 3 | 1.0 | 2 |
    | `CaltropDropper` | "Caltrops" | 3 | 0.8 | 2 |
    | `MissileLauncher` | "Missile" | 2 | 1.0 | 1 |
    | `SatelliteStrike` | "Satellite strike" | 1 | none | 0 |

    Each has `const int MaxCharges`, `const double UseCooldown` (none for the strike) and a ctor `(int startingCharges = <default above>)` passing `rechargeTime` 0.

    `SatelliteStrike` also has `const double SweepSeconds = 1.5`, `bool Targeting`, `double SweepLeft`, `bool TryBegin()` (uses the charge and starts the sweep) and `bool AdvanceSweep(double dt)`, which returns true exactly once, on the step the sweep ends. Its `Update` leaves the sweep alone (only `AdvanceSweep` moves it), so `Loadout.Update` cannot advance it twice.
  - `enum RearWeapon { Oil, Smoke, Caltrops }`, `enum WeaponKind { Oil, Smoke, Caltrops, Missile, Strike }`, `enum SupplyItem { Oil, Smoke, Caltrops, Missile, Strike, Armor }` (all in `WeaponKinds.cs`).
  - `sealed record LoadoutDefinition(int Oil = 5, int Smoke = 2, int Caltrops = 2, int Missiles = 1, int Strikes = 0) { public static LoadoutDefinition Default { get; } = new(); }`
  - `sealed class Loadout`:
    - ctors `Loadout(LoadoutDefinition? start = null)` and `Loadout(int startingOil)` (= `Default with { Oil = startingOil }`)
    - weapons: `MachineGuns Guns`, `OilDropper Oil`, `SmokeLauncher Smoke`, `CaltropDropper Caltrops`, `MissileLauncher Missiles`, `SatelliteStrike Strike`
    - selection: `RearWeapon SelectedRear` (starts Oil), `ChargedWeapon Rear(RearWeapon w)`, `ChargedWeapon SelectedRearWeapon`, `void CycleRear()` (Oil → Smoke → Caltrops → Oil)
    - `void Update(double dt)` updates every weapon except the guns
  - `Hazard` gains `bool Hostile { get; set; }` (dropped by an enemy).
  - `sealed class SmokeCloud : Hazard`:
    - `const double HalfLength = 15, HalfWidth = 6, Lifetime = 5, Drag = 10`
    - state: `double Age`, `double Speed`, `double PrevDistance`
    - `bool Covers(Vehicle v)`: |Δdistance| < HalfLength + Length/2 and |Δlateral| < HalfWidth + Width/2
    - `Update`: Age += dt; Speed = max(0, Speed − Drag·dt); Distance += Speed·dt; expires at Age ≥ Lifetime − 1e-9 (600 steps of 1/120 s must count as 5 s) or 600 m behind.
    - it moves, so it is created with `PrevDistance = Distance` (D18); Task 2 refreshes it in `CapturePrev`.
  - `sealed class Caltrops : Hazard`:
    - `const double HalfWidth = 2.5, HalfLength = 1.5, Lifetime = 30`
    - state: `double Life = Lifetime`, `bool HitPlayer`
    - `bool Covers(Vehicle v)` with the same rule as SmokeCloud
    - expires at Life ≤ 0 or 600 m behind
  - `ActionKind` gains `CycleRear, FireMissile, FireStrike`, appended after `Restart`.
  - `sealed record WeaponUsed(double Time, WeaponKind Weapon, int OwnerId) : GameEvent(Time)`.
  - `World` (in `World.Arsenal.cs`): `void DropRear()` drops the selected weapon.
    - Oil keeps its existing placement and messages.
    - Smoke: a `SmokeCloud` at Distance − 6, at the player's Lateral, with Speed = 0.5·Player.Speed. Message "SMOKE SCREEN".
    - Caltrops: at Distance − 5, at the lane centre nearest the player. Message "CALTROPS DROPPED".
    - Empty: "NO SMOKE LEFT" / "NO CALTROPS LEFT" (oil keeps "NO OIL LEFT (recharging)"). Cooling down: "SMOKE NOT READY" / "CALTROPS NOT READY".
    - Nothing at all happens while the player is Wrecked.
    - Each drop publishes `WeaponUsed`.
  - `void CycleRear()` publishes "REAR WEAPON: {NAME} ({n} LEFT)" (Info). `Handle` routes `DropRear` and `CycleRear`; `FireMissile` and `FireStrike` arrive in Tasks 5 and 6.
  - `StepOnce` calls `Loadout.Update(Step)` instead of `Loadout.Oil.Update(Step)`.

- [ ] **Step 0: Create the branch** `git switch -c plan-2-arsenal-and-bosses plan-1-foundation`, then run the suite. Expected: all pass.
- [ ] **Step 1: Write failing tests** (`Dt = 1.0/120`, `World W() => TestWorlds.World(track: TestWorlds.Straight())`)

```csharp
[Fact] public void Only_oil_recharges() { var l = new Loadout(new LoadoutDefinition(4, 1, 1, 1, 1)); l.Smoke.TryUse(); l.Caltrops.TryUse(); l.Missiles.TryUse(); for (int i = 0; i < 120 * 60; i++) l.Update(Dt); Assert.Equal(5, l.Oil.Charges); Assert.Equal(0, l.Smoke.Charges); Assert.Equal(0, l.Caltrops.Charges); Assert.Equal(0, l.Missiles.Charges); Assert.Equal(1, l.Strike.Charges); }
[Fact] public void Capacities_are_5_3_3_2_1_and_restock_reports_what_fits() { var l = new Loadout(new LoadoutDefinition(5, 2, 3, 1, 0)); Assert.Equal(new[] { 5, 3, 3, 2, 1 }, new[] { l.Oil.Capacity, l.Smoke.Capacity, l.Caltrops.Capacity, l.Missiles.Capacity, l.Strike.Capacity }); Assert.Equal(1, l.Smoke.Restock(3)); Assert.Equal(3, l.Smoke.Charges); Assert.Equal(0, l.Caltrops.Restock(1)); }
[Fact] public void Starting_charges_are_clamped() { Assert.Equal(3, new SmokeLauncher(9).Charges); Assert.Equal(0, new MissileLauncher(-1).Charges); }
[Fact] public void CycleRear_goes_oil_smoke_caltrops_and_back_and_says_so() { /* W(); Handle(CycleRear) → SelectedRear Smoke and Message "REAR WEAPON: SMOKE SCREEN (2 LEFT)"; two more → Oil */ }
[Fact] public void Smoke_drops_6_m_behind_drifting_at_half_speed() { /* player Distance 100, Lateral 1.5, Speed 40; select Smoke; DropRear → one SmokeCloud: Distance 94, Lateral 1.5, Speed 20, Hostile false; Message "SMOKE SCREEN"; WeaponUsed(Smoke, 0) */ }
[Fact] public void Caltrops_drop_5_m_behind_on_the_nearest_lane() { /* Lateral 3.6 → Caltrops at Distance 95, Lateral 5; "CALTROPS DROPPED" */ }
[Fact] public void Empty_rear_weapon_says_so_and_drops_nothing() { /* Loadout smoke 0; select Smoke; DropRear → no hazard; "NO SMOKE LEFT" */ }
[Fact] public void Rear_weapon_in_cooldown_says_not_ready() { /* select Caltrops; DropRear twice in one step → Charges 1, second says "CALTROPS NOT READY" */ }
[Fact] public void Wrecked_player_drops_nothing() { /* Player.State = Wrecked; for each RearWeapon: select with w.Loadout.CycleRear() (Loadout's own method publishes nothing), Handle(DropRear) → Hazards empty, charges unchanged, no Message in Events */ }
[Fact] public void Smoke_cloud_drifts_slows_and_clears_after_5_s() { /* SmokeCloud{Distance 0, Speed 20}, player at 0: after 2 s Speed 0, Distance 20 ± 0.1; not Expired at 4.99 s; Expired at 5.0 s */ }
```
- [ ] **Step 2: Run** `dotnet test tests/NightPursuit.Core.Tests --filter FullyQualifiedName~RearWeaponTests`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement** the interfaces above.
- [ ] **Step 4: Run the whole suite.** Expected: all pass, including `OilTests`.
- [ ] **Step 5: Commit** `feat: charged rear weapons, smoke and caltrop drops, loadout selection`.

### Task 2: Smoke blinds AI drivers

**Files:**
- Create: `Drivers/BlindDriver.cs`
- Modify: `Vehicles/AgentCar.cs`, `Drivers/WorldView.cs`, `Simulation/World.cs` (`StepAgents`, `CapturePrev`)
- Test: `tests/NightPursuit.Core.Tests/SmokeTests.cs`, `InterpolationTests.cs` (add)

**Interfaces:**
- Consumes: `SmokeCloud.Covers` (Task 1), `IDriver.Decide(AgentCar self, WorldView view, double dt)`, `CarPhysics.StepAgent(Vehicle, DriveIntent, double)`.
- Produces:
  - `AgentCar` is no longer `sealed` (Task 10 derives `BossCar` from it). It gains `double Blinded`, `double WanderLateral` and `double WanderTimer`.
  - `sealed class BlindDriver : IDriver`:
    - `public static readonly BlindDriver Instance`
    - TargetSpeed = `max(15, 0.6·CruiseSpeed)`
    - every 0.8 s (WanderTimer) WanderLateral = `clamp(Lateral + (2·rng − 1)·4, −6, 6)`
    - TargetLateral = WanderLateral; never fires or drops
  - `WorldView.CanSeePlayer(AgentCar self) => self.Blinded <= 0`
  - `World.StepAgents`, before deciding:
    - if any SmokeCloud covers the car, Blinded = 2.0. The first step it covers a car that was not blinded publishes "{KIND} LOST YOU IN THE SMOKE" (Good).
    - otherwise Blinded = max(0, Blinded − Step)
    - a Driving car with Blinded > 0 uses `BlindDriver.Instance` instead of its own driver. Its `DriverState` is left untouched, so it resumes its plan afterwards.
  - `CapturePrev` also snaps `SmokeCloud.PrevDistance`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Pursuer_in_smoke_loses_the_player_and_slows() { /* straight; player 30 m/s held; pursuer at −30, lane 0, CruiseSpeed 40; SmokeCloud at −30 (Speed 0); after 1 s Blinded > 0, Message "PURSUER LOST YOU IN THE SMOKE" once; after 2 s Speed ≤ 0.6·40 + 0.5 */ }
[Fact] public void Blindness_lasts_2_s_after_leaving_the_smoke() { /* Blinded = 2, no cloud: still > 0 at 1.99 s, 0 at 2.0 s; DriverState instance unchanged throughout */ }
[Fact] public void Blind_car_wanders_but_stays_on_the_road() { /* Blinded refreshed for 10 s: |Lateral| ≤ 6.01 every step; ≥ 5 distinct WanderLateral values */ }
// InterpolationTests.cs
[Fact] public void Smoke_prev_distance_equals_its_distance_at_the_start_of_the_latest_step() { /* drop smoke (PrevDistance == Distance at once); then for 60 frames of 1/75 s: record Distance before each step via afterStep → after Advance, PrevDistance equals the Distance recorded at the start of the last step ± 1e-12 (copy the Wreck_prev_distance pattern) */ }
```
- [ ] **Step 2: Run** with `--filter "FullyQualifiedName~SmokeTests|FullyQualifiedName~InterpolationTests"`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: smoke blinds AI drivers`.

### Task 3: Car-to-car contact for every pair, and keeping clear of the car ahead

**Files:**
- Modify: `Simulation/Collisions.cs`, `Drivers/DriverHelpers.cs`, `Vehicles/AgentCar.cs`, `Simulation/World.cs` (`StepAgents`)
- Test: `tests/NightPursuit.Core.Tests/CollisionTests.cs` (add), `DriverTests.cs` (add)

**Interfaces:**
- Consumes: `World.Spin(Vehicle, int dir, string reason)`, `World.Destroy(AgentCar, KillCause)`, `Collisions.Resolve(World)`, `DriverHelpers.AvoidWrecks`, `BlindDriver` (Task 2).
- Produces:
  - `Collisions.Resolve` gains an agent-vs-agent pass. It sorts a reused scratch list by (Distance, Id) and checks each car only against later cars with ΔDistance < 6.5 m.
    - **Rear-end:** the follower is put at the leader's rear edge; follower Speed = max(0, leader.Speed − 2); both lose 0.5 armor.
    - **Sideswipe:** separate the cars by half the overlap each; each gets PushVel ±(2.5 + 0.8·closing). If the closing lateral speed is > 4, every Driving car of the pair spins away from the other, with reason "{A} AND {B} COLLIDED".
    - Both cars get ContactCooldown 0.25; a pair is skipped while either has a cooldown.
    - Armor ≤ 0 → `Destroy(car, KillCause.Rammed)` (no wreck, D9).
    - Only cars with `IsOnRoadDriving` take part.
  - `sealed class CarAheadMemory` (in `DriverHelpers.cs`): `bool Active`, `double Lateral`, `double Speed`, `bool Follow`, `double Timer`. `AgentCar` gains `CarAheadMemory AheadMemory { get; } = new()`.
  - `public static DriveIntent DriverHelpers.KeepClearOfCarAhead(AgentCar self, WorldView view, DriveIntent wish, CarAheadMemory memory, double reactionDelay, double dt)`:
    - **Car ahead:** the nearest other agent with `IsOnRoadDriving` whose rear is 0–25 m ahead of this car's front (gap = ΔDistance − (both Lengths)/2), with |ΔLateral| < (both Widths)/2 + 0.3, and that this car is closing on (`self.Speed > other.Speed − 0.5`). The player is never a car ahead: the Blocker and the Armored aim at you on purpose.
    - No car ahead, or `wish.TargetLateral` already leaves that car's lane band → the wish passes unchanged.
    - Otherwise, a neighbouring lane centre (the `Track.LaneCentres` entry 5 m either side of this car's nearest lane) is clear when no other agent is within 25 m ahead or 10 m behind in its band and `AvoidWrecks` would not move the car off it. The clear lane nearest `wish.TargetLateral` wins (ties: nearest the car) and replaces TargetLateral. With no clear lane, TargetSpeed = min(wish.TargetSpeed, other.Speed) (it follows).
    - **Reaction delay** as in `SteerAroundWrecks`: a changed answer takes over only after `reactionDelay` seconds; until then the previous answer holds.
  - `public static double DriverHelpers.ReactionDelay(AgentKind kind)`: Runner 0.5, Blocker 0.8, Pursuer 0.3 (the drivers' wreck delays). Tasks 8–10 add Gunner 0.4, Dropper 0.5, Armored 0.6 and Boss 0.3.
  - `World.StepAgents` passes every Driving agent's intent through `KeepClearOfCarAhead` after its driver decides, except a car using `BlindDriver` (a blinded car can't see the car ahead, which is how cars collide inside smoke). So every driver, present and future, uses it.
  - If an existing test fails only because two of its cars now keep clear of each other (check `Destroying_a_car_ahead_of_a_pursuer_blocks_it` and the mission frame-split tests first), widen that test's starting gap so it tests what it did before, and note it in the decisions log. Do not weaken the helper.

- [ ] **Step 1: Write failing tests**

```csharp
// CollisionTests.cs. The three contact tests call Collisions.Resolve(w) directly, as the Plan 1 ram tests do: a full step would overwrite LateralVel in StepAgent.
[Fact] public void Agents_closing_fast_side_by_side_both_spin() { /* StillDriver runners at 50 m, laterals −0.8/+0.8, LateralVel +3/−3; Collisions.Resolve → both Spinning, two VehicleSpun, a Message "RUNNER AND RUNNER COLLIDED" */ }
[Fact] public void Gentle_side_contact_pushes_apart_without_spinning() { /* LateralVel +0.5/−0.5 (closing 1 m/s); Collisions.Resolve → both Driving, left car PushVel < 0, right car PushVel > 0, |ΔLateral| ≥ 1.9 − 1e-9 */ }
[Fact] public void Rear_end_between_agents_slows_the_follower() { /* leader 50 m / 20 m/s, follower 46 m / 35 m/s same lateral; Collisions.Resolve → follower Speed == 18 ± 1e-9, follower Distance == 45.6 ± 1e-9, both Armor reduced by 0.5, both ContactCooldown 0.25 */ }
[Fact] public void Broad_phase_with_25_cars_is_deterministic() { /* 25 runners in 3 lanes every 12 m ahead; 30 s of Advance twice from seed 4 → equal StateHash */ }
// DriverTests.cs (player stopped at 0 on a straight; cars 200 m and more ahead, so the Blocker is beyond its 140 m reach and just cruises)
[Fact] public void Fast_runner_behind_a_slow_blocker_never_rear_ends_it() { /* Blocker at 260 lane 0, CruiseSpeed 25; Runner at 200 lane 0, CruiseSpeed 50, RunnerDriver State Target 0 and LaneTimer 100 (it wants to stay in lane 0); 20 s → no contact between them on any step (both Armor unchanged, ContactCooldown never > 0, no VehicleSpun); at the end the runner is either ahead of the blocker or behind it with gap ≥ 0 */ }
[Fact] public void Boxed_in_car_follows_at_the_leaders_speed() { /* as above plus StillDriver cars at 25 m/s level with the blocker in lanes −5 and +5 → after 10 s the runner's Speed == 25 ± 0.5, Lateral 0 ± 0.3, gap to the blocker > 0, no contact */ }
[Fact] public void Runner_changes_to_a_clear_lane_after_its_reaction_delay() { /* the first test's setup: the runner's TargetLateral leaves 0 no sooner than 0.5 − 1e-9 s after the blocker first came within 25 m, and it passes the blocker within 20 s */ }
[Fact] public void Blinded_car_does_not_keep_clear() { /* the first test's setup with the runner's Blinded refreshed every step → KeepClearOfCarAhead is not applied (the intent equals BlindDriver's) */ }
```
- [ ] **Step 2: Run** with `--filter "FullyQualifiedName~CollisionTests|FullyQualifiedName~DriverTests"`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass, including the determinism and frame-split tests.
- [ ] **Step 5: Commit** `feat: car-to-car contact for every pair; AI keeps clear of the car ahead`.

### Task 4: Caltrops, punctures, and enemy hazards against the player

**Files:**
- Create: `Simulation/HazardContacts.cs`
- Modify: `Vehicles/AgentCar.cs`, `Vehicles/PlayerCar.cs`, `Hazards/OilSlick.cs`, `Simulation/Collisions.cs`, `Simulation/World.cs`, `Simulation/GameEvent.cs`
- Test: `tests/NightPursuit.Core.Tests/CaltropTests.cs`

**Interfaces:**
- Consumes: `Caltrops.Covers`, `Hazard.Hostile` (Task 1), `World.DamagePlayer`, `World.Destroy`.
- Produces:
  - `AgentCar` gains `bool Punctured` and `double PunctureSeconds` (counts up from the puncture).
  - `PlayerCar` gains `double GripLoss` and `double PunctureLeft` (both count down), and `const double PunctureSpeedCap = 100 / Units.MphPerMs` (44.704 m/s).
  - `OilSlick` gains `bool HitPlayer`.
  - `sealed record VehiclePunctured(double Time, int VehicleId) : GameEvent(Time)`.
  - `static class HazardContacts { static void Resolve(World w) }`, called at the end of `Collisions.Resolve`:
    - **Agents vs caltrops (any owner):** a car with `IsOnRoadDriving`, not yet punctured, that a Caltrops covers becomes Punctured. Publish `VehiclePunctured` and "{KIND} PUNCTURED" (Good). The caltrops stay where they are.
    - **Player vs hostile oil** (`Hostile`, `!HitPlayer`, overlapping by the agent oil rule): set HitPlayer and GripLoss = 1.0. PushVel ±3 (side from `Rng`), Speed ×0.9, Shake ≥ 0.4. Message "OIL - NO GRIP" (Bad).
    - **Player vs hostile caltrops** (`!HitPlayer`, `Covers(Player)`): set HitPlayer and `DamagePlayer(10, "PUNCTURED")`, then PunctureLeft = 5.
    - The player's own hazards (`Hostile == false`) never touch the player.
  - `World.StepAgents`, for a Driving punctured agent: it takes its driver's intent, then replaces it. `PunctureSeconds += Step` for every punctured agent that is on the road.
    - TargetSpeed = max(0, Speed − 5·Step).
    - TargetLateral = Lateral + side·0.5·SideSpeed/1.8 + 0.8·sin(2π·1.5·PunctureSeconds).
    - side = sign(Lateral), or the car's `Side` when |Lateral| < 0.5.
  - `Collisions` off-road crash rule: `|Lateral| > OffRoadLimit (= Track.RoadHalfWidth + 1 = 8.5) && (Spinning || |PushVel| > 2 || Punctured)` → `Destroy(RanOffRoad)` (no wreck, WRK-2).
  - `World.StepOnce` (player, before `StepPlayer`):
    - steering passed in = 0 while GripLoss > 0
    - while PunctureLeft > 0: steering += 0.15·sin(4π·Time) (clamped to ±1), and when Speed ≥ `PunctureSpeedCap` the throttle is cut and, while Speed > the cap, an automatic brake is added: pedals become `(0, max(pedals.Brake, auto))` with `auto = max(0, 4.5 − coast) / Feel.BrakeDecel`, where `coast = 0.6 + 1.6·Speed/Feel.MaxSpeed` is `CarPhysics`'s coasting drag with the throttle at 0. So the total slowdown on the flat is 4.5 m/s² (`const double PunctureBrakeDecel = 4.5` on `PlayerCar`), and a harder brake of your own wins
    - both timers count down by Step

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Caltrops_puncture_a_car_that_crosses_them() { /* Caltrops at 100 lane 0; StillDriver runner at 90 lane 0, 30 m/s → within 0.5 s Punctured, VehiclePunctured, "RUNNER PUNCTURED" */ }
[Fact] public void Caltrops_cover_one_lane_only() { /* same, runner at lane +5 → never Punctured */ }
[Fact] public void Punctured_car_limps_off_the_road_and_crashes_without_a_wreck() { /* RunnerDriver car lane +5 at 40 m/s, Punctured = true → within 8 s VehicleDestroyed(RanOffRoad) published; no Wreck in Hazards; Lateral > 8.5 at the end */ }
[Fact] public void Punctured_car_in_the_centre_lane_heads_for_its_own_side() { /* Lateral 0, Side −1 → crashes with Lateral < −8.5 */ }
[Fact] public void Player_ignores_own_oil_and_caltrops() { /* non-hostile slick and caltrops under the player for 1 s → GripLoss 0, PunctureLeft 0, Armor 100 */ }
[Fact] public void Hostile_oil_takes_grip_for_1_s_once() { /* straight, player 40 m/s, hostile slick at player → GripLoss 1.0, Speed 36 ± 0.1, "OIL - NO GRIP"; SteerDir +1 held 0.9 s → |Heading| < 1e-9; still on the slick after 1.2 s → no second message; Heading > 0 by 1.3 s */ }
[Fact] public void Hostile_caltrops_cost_10_armor_and_brake_gently_to_100_mph() { /* straight flat road, player 67 m/s, Gas held, hostile caltrops at player → Armor 90, PunctureLeft 5; while Speed > 44.704: Throttle == 0 and each step's slowdown is 4.5 ± 0.05 m/s²; after 4.5 s Speed == 67 − 4.5·4.5 = 46.75 ± 0.3; after 5.1 s PunctureLeft 0 and Throttle > 0. Same start with Brake held → slowdown ≥ Feel.BrakeDecel (your own brake wins) */ }
[Fact] public void Hostile_caltrops_cap_a_slower_car_at_100_mph() { /* player 40 m/s, Gas held, hostile caltrops → Speed ≤ 44.704 + 0.1 on every step for 5 s, and Brake == 0 on every step at or below the cap (it only cuts the throttle there); Speed > 45 within 3 s after PunctureLeft reaches 0 */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~CaltropTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: caltrop punctures, roadside crashes, enemy hazards against the player`.

### Task 5: Player missiles with lock-on; wrecks from missiles

**Files:**
- Create: `Weapons/Missile.cs`
- Modify: `Simulation/World.Arsenal.cs`, `World.cs` (`Missiles` list, `CapturePrev`, step order), `Simulation/GameEvent.cs`
- Test: `tests/NightPursuit.Core.Tests/MissileTests.cs`, `InterpolationTests.cs` (add)

**Interfaces:**
- Consumes: `MissileLauncher` (Task 1), `RoadMotion.MoveStraight(Track, RoadPose, double step)`, `RoadView.Build(Track, double)` / `At(double d, double lateral)`, `World.Destroy`.
- Produces:
  - `KillCause` gains `Missile` and `Satellite`. Add `public static class KillCauseRules { public static bool LeavesWreck(this KillCause c) => c is KillCause.Gunfire or KillCause.Missile or KillCause.Satellite; }`. `World.Destroy` uses it in place of `cause == KillCause.Gunfire` (WRK-1).
  - `sealed class Missile`:
    - `const double LaunchBoost = 110, Range = 700, TurnRate = 1.5, HitMargin = 0.6, LockRange = 500, LockMin = 15, LockHalfAngle = 25 * Math.PI / 180, MaxHeading = 60 * Math.PI / 180`
    - state: `RoadPose Pose`, `double PrevDistance, PrevLateral, PrevHeading, Speed, Travel`, `int? TargetId`, `bool Dead`
  - `World.Missiles` (`List<Missile>`). `CapturePrev` copies each missile's Pose into its Prev fields (D18); a new missile starts with Prev = its spawn pose.
  - `AgentCar? World.FindMissileTarget()`:
    - picks the nearest (by ΔDistance) `IsOnRoadDriving` car with `Faction == Hostile` and LockMin ≤ ΔDistance ≤ LockRange
    - the car's car-frame angle `atan2(cx, depth)` must be within ±LockHalfAngle
    - cx and depth use a private `RoadView` rebuilt at the player's Distance when stale, then rotated by the player's Heading exactly as `Projector.ToScreen` does
  - `void World.FireMissile()` (via `Handle(FireMissile)`):
    - Wrecked → nothing. No charges → "NO MISSILES". Cooling down → "MISSILE NOT READY".
    - Otherwise `TryUse`; spawn at Pose (Distance + 2.5, Lateral, Heading), Speed = Player.Speed + LaunchBoost, TargetId = target?.Id.
    - Message "MISSILE AWAY - LOCKED ON {KIND}" or "MISSILE AWAY - NO LOCK". Publish `WeaponUsed(Missile, 0)`.
  - Step (after tracers, before hazards):
    - Homing: if the target is still `IsOnRoadDriving`, the heading turns toward `atan2(ΔLateral, max(1, ΔDistance))` at TurnRate·Step, clamped to ±MaxHeading.
    - Movement: `MoveStraight(Speed·Step)`, then Travel += Speed·Step.
    - Hit: a swept hit against every hostile `IsOnRoadDriving` car (the nearest along its path), with lateral tolerance Width/2 + HitMargin → `Destroy(car, KillCause.Missile)`, then Dead. Task 11 adds the boss branch.
    - Dead at Travel > Range or |Lateral| > 60, with Message "MISSILE LOST" (Info). A missile that hits never says it.
  - Generalise the swept test: `static bool SweptHit(double d0, double l0, double d1, double l1, Vehicle car, double margin)`. Tracers keep margin 0.15.

- [ ] **Step 1: Write failing tests** (StillDriver cars, player stopped unless stated)

```csharp
[Fact] public void Lock_picks_the_nearest_hostile_ahead_inside_25_degrees() { /* cars at (12 m, 0) too close, (50 m, −30) at 31° outside, (200 m, +5) inside, (520 m, 0) out of range → FindMissileTarget is the 200 m car */ }
[Fact] public void No_lock_on_cars_behind_or_wrecked() { /* cars at −40 and a Wrecked one at 100 → null */ }
[Fact] public void Missile_homes_onto_a_lane_changer_and_leaves_a_wreck() { /* RunnerDriver car at 150 m lane 0 with its State.Target = 5; FireMissile → within 2.5 s VehicleDestroyed(Missile); one Wreck with |Lateral − car.Lateral| < 1e-9; no "MISSILE LOST" */ }
[Fact] public void Missile_without_lock_flies_straight_and_says_so() { /* no cars; "MISSILE AWAY - NO LOCK"; after 0.5 s Pose.Lateral = launch lateral ± 1e-6 and Heading unchanged */ }
[Fact] public void Missile_dies_after_700_m_and_says_lost() { /* no cars, player stopped: alive at Travel 690, gone by 710; exactly one Message "MISSILE LOST" (Info) */ }
[Fact] public void Fire_missile_with_none_left_or_cooling_says_so() { /* Missiles 0 → "NO MISSILES", no Missile; Missiles 2: fire twice in one step → 1 left, "MISSILE NOT READY"; Wrecked → nothing, no message */ }
[Fact] public void Only_gunfire_missile_and_satellite_leave_wrecks() { Assert.True(KillCause.Gunfire.LeavesWreck()); Assert.True(KillCause.Missile.LeavesWreck()); Assert.True(KillCause.Satellite.LeavesWreck()); Assert.False(KillCause.Rammed.LeavesWreck()); Assert.False(KillCause.RanOffRoad.LeavesWreck()); Assert.False(KillCause.WreckCollision.LeavesWreck()); }
// InterpolationTests.cs
[Fact] public void Missile_prev_pose_equals_its_pose_at_the_start_of_the_latest_step() { /* FireMissile: Prev fields == spawn pose before any step; then 1/75 s frames: after each Advance, PrevDistance/PrevLateral/PrevHeading equal the pose recorded at the start of the last step ± 1e-12 */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~MissileTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: homing missiles with lock-on; missile kills leave wrecks`.

### Task 6: Satellite strike

**Files:**
- Modify: `Simulation/World.Arsenal.cs`, `Simulation/GameEvent.cs`
- Test: `tests/NightPursuit.Core.Tests/StrikeTests.cs`

**Interfaces:**
- Consumes: `SatelliteStrike.TryBegin/AdvanceSweep` (Task 1), `World.Destroy`, `KillCause.Satellite` (Task 5).
- Produces:
  - `World` constants: `const double StrikeAhead = 400, StrikeBehind = 150`.
  - `bool World.InStrikeRange(AgentCar a)`: hostile, `IsOnRoadDriving`, and −150 ≤ ΔDistance ≤ 400.
  - `void World.BeginStrike()` (via `Handle(FireStrike)`):
    - Wrecked → nothing. None held → "NO SATELLITE STRIKE". Already targeting → "STRIKE ALREADY TARGETING".
    - No car in range → "NO TARGETS IN STRIKE RANGE", and the charge is kept.
    - Otherwise `TryBegin`, publish `WeaponUsed(Strike, 0)` and "SATELLITE STRIKE: TARGETING" (Info).
  - Each step, `Loadout.Strike.AdvanceSweep(Step)` returning true → every car in range is destroyed with `Destroy(car, KillCause.Satellite)` (wrecks block lanes, WRK-1). Task 11 adds the boss branch. Then publish `StrikeLanded(Time, int Destroyed)` and "SATELLITE STRIKE: {n} DESTROYED" (Good). This runs even if the player is Wrecked.
  - `sealed record StrikeLanded(double Time, int Destroyed) : GameEvent(Time)`.
  - Spec §11's "satellite strike spares civilians" test belongs to Plan 3. Here the rule is the `Faction == Hostile` filter.

- [ ] **Step 1: Write failing tests** (player and cars stopped, StillDriver)

```csharp
[Fact] public void Strike_lands_after_1_5_s_on_hostiles_from_150_behind_to_400_ahead() { /* cars at −151, −149, +20, +399, +401 with laterals −5,0,5,0,5; FireStrike; at 1.49 s none destroyed; by 1.51 s: VehicleDestroyed(Satellite) ×3, three Wrecks at −149/+20/+399 with their laterals; −151 and +401 still Driving; StrikeLanded(3); "SATELLITE STRIKE: 3 DESTROYED" */ }
[Fact] public void Strike_with_no_targets_is_kept() { /* no cars → "NO TARGETS IN STRIKE RANGE", Strike.Charges 1, Targeting false */ }
[Fact] public void Second_press_during_sweep_does_nothing() { /* FireStrike, 0.5 s, FireStrike → "STRIKE ALREADY TARGETING"; SweepLeft unchanged by the press; lands once */ }
[Fact] public void Strike_lands_even_if_the_player_is_wrecked_during_the_sweep() { /* FireStrike, then Player.Armor = 0 and one step (the player is Wrecked) → after 1.5 s car at +50 destroyed */ }
[Fact] public void Only_one_strike_can_be_held() { /* Strike 1: Restock(2) returns 0; Strike 0: Restock(2) returns 1 */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~StrikeTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: satellite strike with targeting sweep`.

### Task 7: Supply crates and per-mission loadout

**Files:**
- Create: `Hazards/SupplyCrate.cs`, `Missions/CrateDefinition.cs`
- Modify: `Weapons/Loadout.cs`, `Simulation/HazardContacts.cs`, `Simulation/GameEvent.cs`, `Missions/MissionDefinition.cs`, `MissionLoader.cs`, `MissionDirector.cs`, `MissionRun.cs`
- Test: `tests/NightPursuit.Core.Tests/SupplyTests.cs`

**Interfaces:**
- Consumes: `SupplyItem`, `LoadoutDefinition` (Task 1), `HazardContacts.Resolve` (Task 4).
- Produces:
  - `sealed class SupplyCrate : Hazard`:
    - `const double Size = 1.6`, `SupplyItem Item`, `int Amount`
    - `string Label` = `"{NAME} +{n}"`, NAME one of OIL, SMOKE, CALTROPS, MISSILE, STRIKE, ARMOR and n = Amount (25·Amount for Armor): "OIL +2", "SMOKE +1", "MISSILE +1", "STRIKE +1", "ARMOR +25" for Amount 1
    - expires 600 m behind
  - `int Loadout.Restock(SupplyItem item, int amount)` returns the charges added. Armor is not a weapon, so it returns 0.
  - `HazardContacts`: when the player overlaps a crate (|ΔD| < (1.6 + Length)/2, |ΔL| < (1.6 + Width)/2):
    - Armor: Armor = min(100, Armor + 25·Amount). Weapons: `Restock`.
    - Message "SUPPLY: +{n} {ITEM}" (Good, item in singular as in the labels) or "SUPPLY: {ITEM} ALREADY FULL" (Info).
    - The crate is Expired. Publish `SupplyCollected(Time, SupplyItem Item, int Added)`.
    - Agents and tracers ignore crates.
  - `sealed record CrateDefinition(double AtDistance, int Lane, SupplyItem Item, int Amount)`. Lane −1/0/1 maps to `Track.LaneCentres[Lane + 1]`.
  - `MissionDefinition` gains defaulted trailing parameters, so existing positional constructions still compile: `(..., IReadOnlyList<WaveDefinition> Waves, LoadoutDefinition? StartingLoadout = null, IReadOnlyList<CrateDefinition>? Crates = null)`, plus `LoadoutDefinition Loadout => (StartingLoadout ?? LoadoutDefinition.Default) with { Oil = StartingOil }`.
  - Loader:
    - `"loadout": { "oil", "smoke", "caltrops", "missiles", "strikes" }`. Every key is optional and defaults to `Default`. Each value runs from 0 to its capacity. An unknown key → `"loadout.{key} is not a known weapon (oil, smoke, caltrops, missiles, strikes)"`.
    - When `loadout` is present, `StartingOil` = loadout.oil.
    - Both `startingOil` and `loadout` → `"use loadout.oil instead of startingOil, not both"`.
    - `"crates": [{ "atDistance", "lane", "item", "amount" }]`: ascending atDistance, at least 0. Lane must be −1, 0 or 1. Item must be a known `SupplyItem` (same parse rules as wave kinds). Amount 1–3.
    - Messages name the field, e.g. `crates[0].item 'Rocket' is not a known supply item`.
  - `MissionDirector.Update` adds each crate once, when `Player.Distance ≥ AtDistance − 800`.
  - `MissionRun.Rebuild` uses `new Loadout(def.Loadout)`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Driving_through_a_crate_restocks_and_removes_it() { /* Missiles 1; crate Missile×1 2 m ahead in the player's lane (overlap needs |ΔD| < 3) → after one step Missiles 2, crate gone, SupplyCollected(Missile,1), "SUPPLY: +1 MISSILE" */ }
[Fact] public void Full_weapon_crate_is_used_up_and_says_so() { /* Oil 5; Oil crate → "SUPPLY: OIL ALREADY FULL", crate gone */ }
[Fact] public void Armor_crate_repairs_25_each_up_to_100() { /* Armor 60, Armor×2 → 100; SupplyCollected(Armor, 40) */ }
[Fact] public void Agents_and_bullets_pass_through_crates() { /* runner drives over a crate; a tracer flies through it → crate still there, not Expired */ }
[Fact] public void Crate_label_names_item_and_amount() { /* Oil×2 "OIL +2"; Caltrops×1 "CALTROPS +1"; Missile×1 "MISSILE +1"; Armor×1 "ARMOR +25"; Armor×2 "ARMOR +50" */ }
[Fact] public void Loadout_section_loads() { /* loadout {oil 3, smoke 1, caltrops 0, missiles 2, strikes 1} → def.Loadout == new(3,1,0,2,1), StartingOil 3 */ }
[Fact] public void Missing_loadout_uses_starting_oil_and_defaults() { /* Valid JSON → def.Loadout == LoadoutDefinition.Default with { Oil = 5 } */ }
[Theory]
[InlineData("\"loadout\": { \"smoke\": 4 }", "loadout.smoke must be from 0 to 3 (got 4)")]
[InlineData("\"loadout\": { \"missles\": 1 }", "loadout.missles is not a known weapon")]
[InlineData("\"crates\": [ { \"atDistance\": 100, \"lane\": 2, \"item\": \"Oil\", \"amount\": 1 } ]", "crates[0].lane must be -1, 0 or 1")]
[InlineData("\"crates\": [ { \"atDistance\": 100, \"lane\": 0, \"item\": \"Rocket\", \"amount\": 1 } ]", "crates[0].item 'Rocket' is not a known supply item")]
[InlineData("\"crates\": [ { \"atDistance\": 100, \"lane\": 0, \"item\": \"Oil\", \"amount\": 0 } ]", "crates[0].amount must be from 1 to 3 (got 0)")]
public void Bad_loadout_and_crates_name_the_field(string section, string message) { /* Valid with section added; message contains both source name and text */ }
[Fact] public void Starting_oil_and_loadout_together_are_rejected() { /* message contains "use loadout.oil instead of startingOil" */ }
[Fact] public void Crate_appears_800_m_before_its_distance_once() { /* crate at 2000 lane 1: none at player 1199; at 1200 one SupplyCrate Distance 2000 Lateral 5; further Updates add none */ }
[Fact] public void Mission_loadout_sets_every_weapon() { /* Def with StartingLoadout (5,1,3,2,1) → run.World.Loadout charges 5,1,3,2,1 */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~SupplyTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass, including `MissionTests`.
- [ ] **Step 5: Commit** `feat: supply crates and per-mission loadout`.

### Task 8: Gunner: rear guns and enemy tracers

**Files:**
- Create: `Weapons/AgentArms.cs`, `Drivers/GunnerDriver.cs`, `Simulation/World.EnemyFire.cs`
- Modify: `Vehicles/AgentCar.cs` (`AgentKind.Gunner`, `Arms`), `VehicleStats.cs`, `Weapons/Tracer.cs`, `Drivers/DriverHelpers.cs` (`ReactionDelay`), `Simulation/World.cs` (tracer loop, `DamagePlayer`), `Missions/MissionDirector.cs` (spawn speed)
- Test: `tests/NightPursuit.Core.Tests/GunnerTests.cs`

**Interfaces:**
- Consumes: `MachineGuns.TryFire(bool, double)`, `WorldView.CanSeePlayer` (Task 2), `DriverHelpers.ReactionDelay` (Task 3), `SweptHit` (Task 5), `DriverHelpers.SteerAroundWrecks`.
- Produces:
  - `AgentKind` gains `Gunner`. `VehicleStats.Gunner = new(1.9, 4.6, 1.3, 80, 7, 4.5, 9, 600)`. `ReactionDelay(Gunner)` = 0.4.
  - `sealed class AgentArms`: `MachineGuns? RearGuns`, `OilDropper? Oil`, `CaltropDropper? Caltrops` (all `init`), `bool NextDropIsOil = true`, `void Update(double dt)` (updates Oil and Caltrops; the guns time themselves in `TryFire`). `AgentCar.Arms { get; init; } = new()`. `World.StepAgents` calls `a.Arms.Update(Step)` for every agent that is not Wrecked.
  - `AgentCar.Create` picks `VehicleStats.Gunner` and a shared `GunnerDriver` for `AgentKind.Gunner` (its `kind switch` today falls through to Pursuer), and gives a Gunner `Arms = new() { RearGuns = new() }`.
  - `Tracer` gains `double MuzzleSpeed { get; init; } = MachineGuns.MuzzleSpeed`, `double Range { get; init; } = MachineGuns.Range` and `bool Hostile { get; init; }`. The tracer loop adds `t.MuzzleSpeed·Step` to Travel and compares it with `t.Range`.
  - `enum GunnerPhase { Overtake, Hold, Aim, Burst }`.
  - `sealed class GunnerDriver : IDriver`:
    - State: `Phase`, `Timer`, `Target`, `AvoidMemory Avoid`.
    - Overtake (whenever ΔD < 25): speed player + 12; target = the nearest lane other than the player's. Leave for Hold at ΔD ≥ 35.
    - Hold: speed = player + clamp((45 − ΔD)·0.8, −10, 10); target = clamp(player Lateral, ±5). When the 2.5 s timer is up and `CanSeePlayer`, |ΔL| < 2 and 25 ≤ ΔD ≤ 90 → Aim.
    - Aim: 0.6 s, then Burst. On entering Aim it stores its own Lateral as Target; during Aim and Burst it keeps the Hold speed rule but steers to that stored Target, not to you, so leaving its lane during the 0.6 s warning dodges the burst.
    - Burst: 0.45 s with `Fire = true`, then Hold (timer 2.5 s again).
    - Wreck reaction delay 0.4 s.
    - `public static bool IsAiming(AgentCar c)` (Aim or Burst).
  - `World.EnemyFire.cs`, `void StepAgentWeapons(AgentCar a, DriveIntent intent)`: called for every Driving agent after `StepAgent`. With `Arms.RearGuns` present, `TryFire(intent.Fire, Step)` true → a Tracer:
    - Pose (Distance − Length/2 − 0.3, Lateral, π)
    - Speed = max(20, 160 − a.Speed), MuzzleSpeed 160, Range 120
    - OwnerId a.Id, Hostile true
  - Hostile tracers test only the player (margin 0.15) → `DamagePlayer(2, "SHOT BY A GUNNER", messageGap: 1.0)`. They never hit agents. Player tracers are unchanged.
  - `World.DamagePlayer(double amount, string reason, double messageGap = 0)`: `PlayerDamaged` and HitFlash fire on every hit. The `Message` is skipped when the same reason was messaged less than `messageGap` seconds ago.
  - `MissionDirector` spawn speed for Gunner: `max(30, Player.Speed)`.

- [ ] **Step 1: Write failing tests** (straight road, player speed set to 30 each step unless stated)

```csharp
[Fact] public void Gunner_gets_ahead_and_holds_35_to_55_m() { /* gunner at −40 lane 5 → after 15 s ΔD in [35, 55] */ }
[Fact] public void Gunner_aims_0_6_s_before_its_first_shot() { /* gunner at +45 lane 0, State Hold Timer 0 → first hostile Tracer appears ≥ 0.6 − 1e-9 s after Phase became Aim */ }
[Fact] public void Gunner_burst_hits_the_player_in_its_lane() { /* both lane 0 → after one burst Armor == 88, six PlayerDamaged events */ }
[Fact] public void Gunner_burst_misses_one_lane_over() { /* when Phase becomes Burst, teleport player to Lateral 5 (SnapPrev) → Armor 100 */ }
[Fact] public void Gunner_stops_following_you_once_it_aims() { /* gunner Hold at +45 lane 0, player lane 0; from the step Phase becomes Aim, move the player to Lateral 5 → gunner |Lateral| < 0.3 every step until Hold returns; Armor 100 */ }
[Fact] public void Blinded_gunner_does_not_fire() { /* Blinded refreshed for 3 s → no hostile Tracer */ }
[Fact] public void Enemy_tracers_never_hit_agents() { /* StillDriver runner at +20 lane 0 between gunner and player → runner Armor 6 after a burst */ }
[Fact] public void Gunner_hits_message_at_most_once_per_second() { /* one burst: six PlayerDamaged, one Message "SHOT BY A GUNNER" */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~GunnerTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: gunner enemy with rear guns and enemy tracers`.

### Task 9: Dropper and Armored

**Files:**
- Create: `Drivers/DropperDriver.cs`, `Drivers/ArmoredDriver.cs`
- Modify: `Vehicles/AgentCar.cs`, `VehicleStats.cs`, `Drivers/DriverHelpers.cs` (`ReactionDelay`), `Simulation/World.EnemyFire.cs`, `World.cs` (bullet damage), `Simulation/Collisions.cs` (Armored spin threshold), `Missions/MissionDirector.cs`
- Test: `tests/NightPursuit.Core.Tests/DropperArmoredTests.cs`

**Interfaces:**
- Consumes: `AgentArms` (Task 8), `OilSlick`, `Caltrops`, `Hazard.Hostile` (Task 1), `CanSeePlayer` (Task 2), `FireMissile` (Task 5).
- Produces:
  - `AgentKind` gains `Dropper` and `Armored`.
    - `VehicleStats.Dropper = new(2.0, 4.8, 1.8, 75, 6.5, 4.0, 9, 600)`.
    - `VehicleStats.Armored = new(2.3, 5.2, 1.6, 64, 5.0, 3.0, 12, 900)`.
    - A Dropper gets `Arms { Oil = new(5), Caltrops = new(3) }`.
    - `AgentCar.Create` picks these stats and shared `DropperDriver` / `ArmoredDriver` instances for the two kinds. `ReactionDelay`: Dropper 0.5, Armored 0.6.
  - `AgentCar` gains `bool HintShown`.
  - `sealed class DropperDriver : IDriver`:
    - State: `Timer`, `Arming`, `Target`, `Avoid`.
    - **Reaction gap:** `public static double ReactionGap(AgentCar dropper, PlayerCar p, Hazard next)` = the distance from your front bumper to the near edge of the hazard it would drop now: `(dropper.Distance − dropper.Length/2 − 1.5 − half) − (p.Distance + p.Length/2)`, where `half` is `OilSlick.Radius` (2.4) for oil and `Caltrops.HalfLength` (1.5) for caltrops (the kind `NextDropIsOil` would pick). The 1.3 s is measured here, not at the van.
    - Position: hold ΔD at `max(80, 1.5·player.Speed) + 18.5`, with speed player + clamp((want − ΔD)·0.8, −10, 12); target = the lane centre nearest the player.
    - Arming starts when the 3.5 s timer is up and `CanSeePlayer`, |ΔL| < 2.5, ΔD ≤ 200 and `ReactionGap ≥ max(60, 1.3·player.Speed)`. Arming lasts 0.5 s, then `DropRear = true` for one step, but only if the gap rule still holds. Otherwise it cancels and retries in 0.5 s.
    - `public static bool IsArming(AgentCar c)`.
  - `World.StepAgentWeapons`, for `intent.DropRear`:
    - drops oil or caltrops from `Arms` by `NextDropIsOil`, then flips the flag. If the chosen weapon can't be used (empty or cooling down), it drops the other one; if neither can, nothing drops and nothing is published. (Only the oil recharges, so a Dropper drops only oil once its 3 caltrops are gone.) A switched drop rechecks `ReactionGap` for the hazard it really drops and skips the drop if it fails.
    - Hostile = true, at Distance − Length/2 − 1.5
    - oil at the car's Lateral; caltrops at its nearest lane centre
    - publishes `WeaponUsed(kind, a.Id)` and "DROPPER: OIL AHEAD" / "DROPPER: CALTROPS AHEAD" (Bad)
  - `enum ArmoredPhase { Lumber, Chase, BackOff }`.
  - `sealed class ArmoredDriver : IDriver`:
    - Lumber while ΔD > 5: CruiseSpeed, keeping its lane.
    - Chase when behind: speed min(64, player + 8); target = player Lateral.
    - Contact (`ContactCooldown > 0`) → BackOff for 2 s at player − 6, then Chase.
    - Wreck reaction delay 0.6 s.
  - Bullet damage in the tracer loop: Armored takes 0.15 per hit, others 1. The first hit on an Armored car with `!HintShown` publishes "ARMORED: USE A MISSILE, A RAM OR A HAZARD" (Info) and sets HintShown.
  - `Collisions.CarVsCar`: a player sideswipe spins an Armored car only when `playerInto > 8` (every other kind keeps `> 5`). `const double ArmoredSpinInto = 8`.
  - `MissionDirector` spawn speeds: Dropper `max(30, Player.Speed)`; Armored `Player.Speed + 6` behind, `30 + 8·rng` ahead.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory][InlineData(20)][InlineData(45)][InlineData(69)]
public void Dropper_only_drops_where_the_player_has_1_3_s_to_react(double v) { /* player speed held at v, dropper spawned 120 m ahead; 40 s; ≥ 2 drops; for each new hostile hazard, at the step it appears: (hazard.Distance − half) − (player.Distance + 2.2) ≥ max(60, 1.3·v) − 1e-6, half = 2.4 for oil and 1.5 for caltrops */ }
[Fact] public void Dropper_arms_for_0_5_s_before_each_drop() { /* IsArming true for ≥ 0.5 − 1e-9 s immediately before every drop */ }
[Fact] public void Dropper_alternates_oil_and_caltrops_marked_hostile() { /* first four drops: OilSlick, Caltrops, OilSlick, Caltrops; all Hostile; caltrops Lateral is a lane centre; each drop publishes WeaponUsed(kind, dropper.Id) and "DROPPER: OIL AHEAD" / "DROPPER: CALTROPS AHEAD" */ }
[Fact] public void Dropper_out_of_caltrops_drops_oil_instead() { /* Arms.Caltrops used up (3 TryUse calls with Update between), NextDropIsOil false → the next drop is an OilSlick */ }
[Fact] public void Armored_takes_0_15_per_bullet_and_hints_once() { /* 10 tracer hits → Armor 10.5 ± 1e-9; the hint Message exactly once */ }
[Fact] public void Missile_destroys_an_armored_car_outright() { /* Armored 150 m ahead, FireMissile → VehicleDestroyed(Armored, Missile), a Wreck */ }
[Fact] public void Armored_rams_from_behind_then_backs_off() { /* Armored at −40 lane 0, player 30 m/s → within 10 s PlayerDamaged "HIT FROM BEHIND"; 1 s later ΔD < −5; Chase again within 2.5 s */ }
[Fact] public void Armored_ahead_lumbers_at_cruise_speed() { /* Armored +100, CruiseSpeed 32, player 50 → its Speed stays 32 ± 0.5 until passed */ }
[Fact] public void Armored_needs_a_hard_ram_to_spin() { /* Collisions.Resolve directly, as Hard_side_ram_spins_target: Armored at 1.8 m with player LateralVel 6 → still Driving (a Runner spins at 6); a fresh Armored with LateralVel 9 → Spinning */ }
[Fact] public void Caltrops_puncture_an_armored_car_off_the_road() { /* Armored over caltrops → within 10 s VehicleDestroyed(RanOffRoad) */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~DropperArmoredTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass, including `Hard_side_ram_spins_target`.
- [ ] **Step 5: Commit** `feat: dropper and armored enemies`.

### Task 10: Boss car, phases and boss drivers

**Files:**
- Create: `Vehicles/BossCar.cs`, `Drivers/BossDriver.cs`, `Drivers/BossPursuitDriver.cs`, `Missions/BossDefinition.cs`, `Simulation/World.Boss.cs`, `Simulation/BossDamage.cs`
- Modify: `Vehicles/AgentCar.cs` (`AgentKind.Boss`, `DriverFor`), `VehicleStats.cs`, `Drivers/RunnerDriver.cs` (interval per instance), `Drivers/DriverHelpers.cs` (`ReactionDelay`), `Simulation/World.cs` (`IsGone`, `StepAgents`), `Collisions.cs` (off-road rule skips the boss), `GameEvent.cs`
- Test: `tests/NightPursuit.Core.Tests/BossTests.cs`

**Interfaces:**
- Consumes: every driver class, `AgentArms`, `World.Destroy`, `DriverHelpers.SteerAroundWrecks`.
- Produces:
  - `AgentKind` gains `Boss`. `VehicleStats.Boss = new(2.4, 5.6, 1.5, 88, 9, 5.5, 180, 5000)`. `ReactionDelay(Boss)` = 0.3. `public static IDriver AgentCar.DriverFor(AgentKind kind)` returns the shared driver instance for each of the six behaviours.
  - `RunnerDriver` gets a ctor `RunnerDriver(double minInterval = 2.5, double extraInterval = 4)` (lane hop every `minInterval + extraInterval·rng` s; the default keeps Plan 1's 2.5–6.5 s) and `public static readonly RunnerDriver Weaving = new(1.0, 0.8)` (1.0–1.8 s).
  - `sealed record BossPhaseDefinition(AgentKind Behaviour, double EndsBelow, bool FiresMissiles = false)`.
  - `sealed record BossDefinition(string Name, double Health, IReadOnlyList<BossPhaseDefinition> Phases)`.
  - `sealed class BossCar : AgentCar`:
    - `BossCar(BossDefinition def)` with stats `VehicleStats.Boss with { Armor = def.Health }`
    - `static BossCar Create(int id, BossDefinition def, double distance, double lateral, double speed)`: CruiseSpeed 52, arms with RearGuns, Oil(5) and Caltrops(3), Driver = `BossDriver.Instance`, Engaged false, then `SnapPrev`
    - `BossDefinition Definition`, `int PhaseIndex`, `BossPhaseDefinition Phase`, `double MaxHealth`, `double HealthFraction => Armor / MaxHealth`
    - `double Shield`, `double SlowLeft`, `bool ArmorOpen => State == VehicleState.Spinning`, `Wreck? LastWreck`, `bool Engaged`
  - `enum BossPursuitPhase { Attack, Overtake, HoldAhead, DropBack }`.
  - `sealed class BossPursuitDriver : IDriver` (call 27; a boss's Pursuer behaviour), `public static readonly BossPursuitDriver Instance`. State: `Phase`, `Timer`, `Target`, `AvoidMemory Avoid`. It starts in Attack when ΔD ≤ 60, else in DropBack.
    - **Attack** (3.5 s timer): speed = player + clamp(−ΔD·1.5, −8, 8); target = clamp(player Lateral + Side·2.7, ±6.3), and in the last 2.4 s player Lateral + Side·0.8 (it rams, as a Pursuer does). Contact (`ContactCooldown > 0`) or the timer → Overtake.
    - **Overtake:** speed = min(88, player + 12); target = the lane centre farthest from the player (at least 2.5 m from you; ties go to its own side). At ΔD ≥ 60 → HoldAhead.
    - **HoldAhead** (2.5 s): speed = player + clamp((70 − ΔD)·0.8, −10, 10); keeps its lane. Then DropBack.
    - **DropBack:** speed = player − 8; target = clamp(player Lateral + Side·2.7, ±6.3). At ΔD ≤ 8 → Attack, with Side = the side of you it is on.
    - Wreck reaction delay 0.3 s. Ordinary Pursuers keep `PursuerDriver`.
  - `sealed class BossDriver : IDriver`:
    - `public static readonly BossDriver Instance`
    - **Arrival** (call 21): while `!Engaged`, TargetSpeed = max(0, player − 10) and TargetLateral = its own Lateral (through `SteerAroundWrecks`). The first step with ΔD ≤ 140 sets Engaged, and so does the first hit of any kind: `DamageBoss` (even while shielded), `SpinBoss` and the caltrops slow all set it. From then on:
    - the phase driver decides: Pursuer → `BossPursuitDriver.Instance`, Runner → `RunnerDriver.Weaving` with TargetSpeed replaced by player + clamp((70 − ΔD)·0.6, −10, 10) (call 28), every other behaviour → `AgentCar.DriverFor(behaviour)`
    - then: if SlowLeft > 0, TargetSpeed ×0.6
    - leash: ΔD < −120 → TargetSpeed = min(88, player + 25); ΔD > 600 → TargetSpeed = player − 15
  - `static class BossDamage`:
    - `const double MissileFraction = 0.20, StrikeFraction = 0.35, WreckFraction = 0.10`
    - `const double OilSpinSeconds = 1.5, OpenArmorGunFactor = 2, ArmoredPhaseGunFactor = 0.25`
    - `const double CaltropSlowSeconds = 4, CaltropSlowFactor = 0.6, PhaseShieldSeconds = 1.5`
  - `World.Boss.cs`:
    - `void DamageBoss(BossCar b, double amount, KillCause cause, string reason)`:
      - while Shield > 0 → ignored
      - floor = `Phase.EndsBelow · MaxHealth`; Armor = max(floor, Armor − amount); publish `BossDamaged(Time, double Amount, string Reason)`
      - last phase at Armor ≤ 0 → `Destroy(b, cause)`
      - otherwise, at Armor ≤ floor (with floor > 0) → `NextPhase(b)`: PhaseIndex++, DriverState = null, Shield = 1.5. Publish `BossPhaseChanged(Time, int Phase, int PhaseCount, AgentKind Behaviour)` (1-based) and "BOSS PHASE {n} OF {m}: {BEHAVIOUR}" (Info).
    - `void SpinBoss(BossCar b)`: Spinning, StateTimer 1.5, SpinRate ±7, PushVel 0, Speed ×0.85; publishes "BOSS SPUN OUT - ARMOR OPEN" (Good).
  - The off-road crash rule skips a BossCar. After `StepAgent`/`StepSpinning`, `StepAgents` clamps a boss's Lateral to ±(7.5 − Width/2) = ±6.3 and zeroes an outward PushVel. Shield and SlowLeft count down there.
  - `IsGone`: a BossCar is removed only when destroyed (the removals list, or Wrecked and burnt out), never for distance.

- [ ] **Step 1: Write failing tests** (`BossDefinition Two => new("Test", 100, [new(AgentKind.Blocker, 0.5), new(AgentKind.Pursuer, 0)])`; `Three` = Gunner 0.66, Dropper 0.33, Pursuer 0; bosses start Engaged unless stated; damage goes through `DamageBoss` directly, as the sources are wired in Task 11)

```csharp
[Fact] public void Phase_ends_below_its_threshold_and_swaps_behaviour() { /* Two: DamageBoss(51) → PhaseIndex 1, Phase.Behaviour Pursuer, DriverState null, BossPhaseChanged(2, 2, Pursuer), "BOSS PHASE 2 OF 2: PURSUER" */ }
[Fact] public void One_blow_never_skips_a_phase() { /* Three, Armor 67: DamageBoss(35) → Armor 66 exactly, PhaseIndex 1; DamageBoss(20) in the same step → ignored (Shield); after 1.5 s DamageBoss(20) → 46 */ }
[Fact] public void Boss_dies_only_when_the_last_phase_reaches_zero() { /* Two: DamageBoss(20) until destroyed → VehicleDestroyed(Boss) only after PhaseIndex 1 */ }
[Fact] public void Boss_never_runs_off_the_road() { /* Lateral 9, PushVel 7, Spinning → next step Lateral 6.3, not destroyed */ }
[Fact] public void Boss_is_never_removed_for_distance_and_catches_up() { /* boss at −700 → still in Agents; another at −300, player 50 m/s held → ΔD > −120 within 10 s */ }
[Fact] public void Boss_eases_in_at_your_speed_minus_10_until_within_140_m_or_hit() { /* not Engaged, player 50 m/s held, boss at +300 with Speed 40 → Speed == 40 ± 0.5 and Lateral unchanged while ΔD > 140; Engaged becomes true at ΔD ≤ 140, 16 ± 0.5 s after the start, then the Blocker phase moves it toward your lane; a second boss at +300: DamageBoss(1) → Engaged on that step; a third: SpinBoss → Engaged */ }
[Fact] public void Pursuit_boss_rams_then_pulls_ahead_into_gun_range() { /* Pursuer phase, player 40 m/s held in lane 0, boss at −30 → a PlayerDamaged "SIDESWIPED" within 15 s; within 10 s after it, ΔD ≥ 60 with the boss's |Lateral − player Lateral| ≥ 2.5 while it passes; over one full cycle the boss is ≥ 10 m ahead for at least 6 s in total */ }
[Fact] public void Runner_boss_weaves_and_stays_ahead() { /* Runner phase, player 50 m/s held, boss at +70 → over 20 s at least 10 changes of its RunnerDriver Target, and 30 ≤ ΔD ≤ 120 throughout */ }
[Fact] public void Ordinary_pursuers_keep_their_plan_1_retreat() { /* a plain AgentKind.Pursuer still uses PursuerDriver (DriverFor(Pursuer) is PursuerDriver), so the Plan 1 retreat tests are unchanged */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~BossTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: boss car, phases, arrival and boss pursuit and weave drivers`.

### Task 11: Boss damage routing (ENM-3)

**Files:**
- Modify: `Simulation/World.cs` (bullet hits), `World.Arsenal.cs` (missile hits, strike), `Collisions.cs` (oil, wrecks, ramming, car contact), `HazardContacts.cs` (caltrops)
- Test: `tests/NightPursuit.Core.Tests/BossDamageTests.cs`

**Interfaces:**
- Consumes: `DamageBoss`, `SpinBoss`, `BossDamage` constants (Task 10), the missile (Task 5), the strike (Task 6), `Caltrops` (Task 1), `OilSlick`, `Wreck`.
- Produces: every damage source routes a `BossCar` through Task 10's rules instead of its usual effect:

    | Source | Effect on the boss |
    |---|---|
    | Gunfire | `1 × (ArmorOpen ? 2 : 1) × (Phase.Behaviour == Armored ? 0.25 : 1)` |
    | Missile | `0.20 · MaxHealth` |
    | Strike | `0.35 · MaxHealth` (the boss counts in `InStrikeRange` and in the strike's `Destroyed` count only if the blow destroys it) |
    | Oil | `SpinBoss` |
    | Caltrops | SlowLeft = 4, never punctured |
    | Wreck (not `LastWreck`) | `0.10 · MaxHealth` (KillCause.WreckCollision), then LastWreck = it, PushVel ±4 toward the side with more room, Speed ×0.7; the wreck no longer destroys it |
    | Ramming and car contact | never spins, no armor loss, never `Destroy(Rammed)`, PushVel capped at ±4 |

- [ ] **Step 1: Write failing tests** (`Two` and `Three` as in Task 10)

```csharp
[Fact] public void Gunfire_takes_one_point_per_bullet() { /* 5 tracer hits → Armor 95 */ }
[Fact] public void Gunfire_does_a_quarter_in_an_Armored_phase() { /* def (Armored 0.5, Pursuer 0): 4 tracer hits → Armor 99 */ }
[Fact] public void Missile_takes_20_percent() { /* missile hit → 80; boss Driving */ }
[Fact] public void Strike_takes_35_percent() { /* def (Blocker 0.1, Pursuer 0); strike in range → 65 */ }
[Fact] public void Strike_and_missile_in_one_step_never_skip_a_phase() { /* Three, Armor 67, a missile timed to hit on the step the strike lands → Armor 66, PhaseIndex 1, Shield 1.5 */ }
[Fact] public void Oil_spins_it_1_5_s_in_place_and_doubles_gunfire() { /* boss over player oil → Spinning, StateTimer 1.5; one hit while spinning → −2; Lateral unchanged ± 1e-9 after 1.5 s; Driving again */ }
[Fact] public void Caltrops_slow_it_for_4_s_without_a_puncture() { /* Punctured false, SlowLeft 4; after 3 s Speed ≤ 0.6·52 + 1; SlowLeft 0 by 4.01 s */ }
[Fact] public void Ramming_pushes_it_aside_but_never_spins_or_wrecks_it() { /* 10 rams, 0.3 s apart: put the player 1.8 m left of the boss at the same Distance with LateralVel +8 and call Collisions.Resolve(w) (a step would overwrite LateralVel), then step 0.3 s → Driving throughout, Armor 100, |PushVel| ≤ 4 + 1e-9 after each ram, |Lateral| ≤ 6.3 + 1e-9, no VehicleSpun or VehicleDestroyed for the boss */ }
[Fact] public void Hitting_a_wreck_costs_10_percent_once_and_it_swerves() { /* boss into a stopped wreck → Armor 90, |PushVel| = 4; overlapping it 1 s → still 90, not Wrecked */ }
[Fact] public void Smoke_blinds_the_boss_like_any_car() { /* SmokeCloud over the boss → Blinded > 0 */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~BossDamageTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: every weapon and contact routes through the boss damage table`.

### Task 12: The boss in the mission

**Files:**
- Create: `content/missions/proving-ground.json`
- Modify: `Missions/MissionDefinition.cs`, `MissionLoader.cs`, `MissionDirector.cs`, `MissionRun.cs`, `Simulation/GameEvent.cs`, `Simulation/World.cs` (`Destroy` message for a BossCar), `content/missions/mission-01.json`, `tests/NightPursuit.Core.Tests/MissionTests.cs` (`Mission_01_loads` gains the boss asserts)
- Test: `tests/NightPursuit.Core.Tests/BossMissionTests.cs`

**Interfaces:**
- Consumes: `BossDefinition`, `BossCar.Create`, `BossCar.Engaged` (Task 10), `CrateDefinition` (Task 7).
- Produces:
  - `MissionDefinition` gains a trailing `BossDefinition? Boss = null`.
  - Loader: `"boss": { "name", "health", "phases": [{ "behaviour", "endsBelow", "missiles"? }] }`.
    - health 20–1000; 2 or 3 phases; `behaviour` is any `AgentKind` except `Boss`
    - endsBelow strictly decreasing, each in [0, 1), and the last exactly 0
    - waves may not use kind `Boss`, and every wave's `atDistance` must be below `approachLength`
    - messages:
      - `boss.phases must have 2 or 3 phases (got 1)`
      - `boss.phases[1].endsBelow must be below boss.phases[0].endsBelow`
      - `boss.phases[2].endsBelow must be 0 for the last phase`
      - `boss.phases[0].behaviour 'Boss' is not a behaviour`
      - `boss.health must be from 20 to 1000 (got 5)`
      - `waves[0].kind 'Boss' cannot be a wave (set the boss in "boss")`
      - `waves[3].atDistance 6500 must be less than approachLength 6000`
  - `enum MissionStage { Approach, Boss }`. `MissionDirector` gains `MissionStage Stage` and `BossCar? Boss`.
    - At `Player.Distance ≥ ApproachLength`: no boss defined → Completed (as before). Otherwise spawn the boss once: `BossCar.Create` at +300, Lateral 0, Speed max(0, player − 10), Engaged false. Publish `BossAppeared(Time, string Name)` and "BOSS AHEAD: {NAME}" (Bad).
    - In the Boss stage no waves spawn and `DistanceRemaining` is 0.
    - `Completed` once `Boss.State == Wrecked`.
    - `World.Destroy` writes "{NAME} DESTROYED +{points}" for a BossCar.
  - `MissionRun.Respawn` does not remove the boss. It always moves it to player + 300 in the centre lane (SnapPrev, DriverState null, Engaged false, so it eases in again at your speed −10 m/s until you are within 140 m or hit it). Health, PhaseIndex and Shield are unchanged (MSN-3).
  - `mission-01.json` gains:
    - `"loadout": { "oil": 5, "smoke": 1, "caltrops": 1, "missiles": 1, "strikes": 0 }`, replacing `startingOil`
    - crates: 1500 lane 1 Missile 1; 2600 lane −1 Smoke 1; 3800 lane 0 Caltrops 1; 5000 lane 1 Armor 1
    - `"boss": { "name": "Black Heron", "health": 180, "phases": [ { "behaviour": "Blocker", "endsBelow": 0.6 }, { "behaviour": "Pursuer", "endsBelow": 0.25 }, { "behaviour": "Runner", "endsBelow": 0 } ] }`
  - `proving-ground.json`:
    - id `proving-ground`, title "Proving Ground", road seed 21, approachLength 4500
    - loadout 5/3/3/2/1
    - waves at 200 Runner×2 +400, 600 Gunner×1 +300, 1200 Dropper×1 +500, 1800 Armored×1 −200, 2400 Pursuer×2 −250, 3000 Blocker×2 +500, 3600 Gunner×1 +400, 4000 Armored×1 +450
    - crates at 500 (1, Smoke 1), 1000 (−1, Caltrops 1), 1600 (0, Missile 1), 2200 (1, Oil 2), 2800 (−1, Strike 1), 3400 (0, Armor 1), 4300 (1, Missile 2)
    - boss "Glass Wasp", 160 health: Gunner 0.66, Dropper 0.33, Pursuer 0 with `"missiles": true`
    - briefing: one original sentence saying it is a test course

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Mission_01_has_a_three_phase_boss_and_a_loadout() { /* Boss.Name "Black Heron", Health 180, phases Blocker 0.6 / Pursuer 0.25 / Runner 0, none FiresMissiles, Loadout (5,1,1,1,0), 4 crates */ }
[Fact] public void Proving_ground_loads_with_every_enemy_type_and_crate_item() { /* six distinct wave kinds, all six SupplyItems, 3 phases, last FiresMissiles */ }
[Theory] /* one InlineData per boss and wave message listed above, each a section that replaces or adds to Valid */ public void Boss_and_wave_errors_name_the_field(string section, string message) { /* MissionFormatException whose Message contains "m.json: " + message (same pattern as Bad_loadout_and_crates_name_the_field) */ }
[Fact] public void Boss_appears_300_m_ahead_when_the_approach_ends() { /* Def(500) with Two, player 40 m/s: at 500 → Stage Boss, Boss.Distance 800, Lateral 0, Speed 30, Engaged false, BossAppeared, DistanceRemaining 0, State Playing */ }
[Fact] public void No_waves_spawn_after_the_boss_appears() { /* programmatic Def(500) with Two and a Runner wave at 600 (the loader would reject it; the director must still ignore it): drive to 700 → Agents contains only the boss; the boss spawned exactly once (one BossAppeared) */ }
[Fact] public void Destroying_the_boss_completes_the_mission() { /* Destroy(boss) → next Update State Completed */ }
[Fact] public void Respawn_puts_the_boss_300_m_ahead_with_its_health_and_phase() { /* boss 50 m ahead, Engaged, Armor 40, PhaseIndex 1; player wrecked → after respawn boss in Agents, Armor 40, PhaseIndex 1, Distance == player + 300 ± 1e-9, Lateral 0, Engaged false; same when it was 500 m ahead */ }
[Fact] public void Weapon_keys_do_nothing_while_paused_or_respawning() { /* Paused: FireMissile, FireStrike, DropRear, CycleRear → charges, SelectedRear, Missiles and Hazards unchanged; same while Respawning */ }
```
`Reaching_approach_length_completes_the_run` (a mission without a boss) must still pass unchanged.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~BossMissionTests|FullyQualifiedName~MissionTests"`. Expected: the new tests FAIL.
- [ ] **Step 3: Implement**, and edit the two mission files.
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: boss stage at the end of the approach; mission 1 boss; proving ground`.

### Task 13: Enemy missiles (ENM-4)

**Files:**
- Create: `Weapons/EnemyMissile.cs`, `Weapons/EnemyMissileRack.cs`
- Modify: `Weapons/AgentArms.cs`, `Vehicles/DriveIntent.cs`, `Vehicles/BossCar.cs`, `Drivers/BossDriver.cs`, `Simulation/World.EnemyFire.cs`, `World.cs` (`EnemyMissiles` list, `CapturePrev`, tracer loop, `StateHash`), `GameEvent.cs`, `Missions/MissionRun.cs` (`Respawn` clears missiles)
- Test: `tests/NightPursuit.Core.Tests/EnemyMissileTests.cs`, `InterpolationTests.cs` (add)

**Interfaces:**
- Consumes: `BossPhaseDefinition.FiresMissiles` (Task 10), `CanSeePlayer`, `DamagePlayer`.
- Produces:
  - `DriveIntent` gains a trailing `bool FireMissile = false`.
  - `sealed class EnemyMissileRack`: `const double Interval = 6`, `double CooldownLeft`, `bool Ready`, `bool TryLaunch()`, `void Update(double dt)`. `AgentArms` gains `EnemyMissileRack? Missiles`, which `BossCar.Create` sets.
  - `sealed class EnemyMissile`:
    - `const double ClosingSpeed = 25, MinLaunchGap = 60, MaxLaunchGap = 160, LateralRate = 6, LockSeconds = 1.0`
    - `const double HitHalfWidth = 1.4, ContactGap = 2.7, Damage = 25, ShotDownPoints = 50`
    - state: `int Id`, `double Distance, Lateral, PrevDistance, PrevLateral, TargetLateral, LaunchTime`, `int Dir` (+1 launched ahead of the player, −1 behind), `bool Locked, Dead`
    - `double TimeToImpact(PlayerCar p) => Math.Max(0, (Math.Abs(Distance − p.Distance) − ContactGap) / ClosingSpeed)`
  - `World.EnemyMissiles` (`List<EnemyMissile>`). `CapturePrev` copies Distance and Lateral into the Prev fields (D18); a new missile starts with Prev = its spawn values.
  - `MissionRun.Respawn` empties `World.EnemyMissiles` and `World.Missiles`, and no enemy missile launches while the player is Wrecked, so nothing unannounced is in flight when the new car starts.
  - `enum MissileResult { Hit, Dodged, ShotDown }`. `EnemyMissileLaunched(double Time, int MissileId)` and `EnemyMissileEnded(double Time, int MissileId, MissileResult Result)`.
  - `BossDriver` sets `FireMissile` when `Engaged && Phase.FiresMissiles && CanSeePlayer && Arms.Missiles.Ready && 60 ≤ |ΔD| ≤ 160`. (`BossPursuitDriver` holds about 70 m ahead, inside this window.)
  - `StepAgentWeapons` launches only if that gap still holds and no enemy missile is in flight. It spawns at the boss's end nearest the player, at the boss's Lateral, then publishes the launch event and "MISSILE INCOMING" (Bad).
  - Each step:
    - Movement (after the player has moved this step): `Distance += (Player.Distance − Player.PrevDistance) − Dir·ClosingSpeed·Step`, so it closes at exactly 25 m/s whatever you do. (`Player.Speed·Step` would be off by `Speed·(1 − cos Heading)`, which shortens the warning from behind.)
    - Steering: while not Locked, TargetLateral = Player.Lateral. Lateral moves toward TargetLateral at 6 m/s. When TimeToImpact ≤ 1.0, it sets Locked (and stops tracking).
    - Arrival (|ΔD| ≤ ContactGap or ΔD changed sign): |ΔL| < 1.4 → `DamagePlayer(25, "MISSILE HIT")`, Hit. Otherwise Dodged, with "MISSILE DODGED" (Good). Then Dead.
  - Player tracers are swept against enemy missiles (a target 1.2 m long and 1.6 m wide, margin 0.15) before agents. The guns sit 0.7 m either side of your centre line, so a target narrower than about 1.2 m would slip between the two streams of a missile dead in your lane. A hit kills the missile and scores +50: ShotDown, "MISSILE SHOT DOWN +50" (Good).
  - `StateHash` also mixes every Missile and EnemyMissile Distance and Lateral, and `Hazards.Count`.

- [ ] **Step 1: Write failing tests** (`MissileBoss` = def (Pursuer 0.5 FiresMissiles, Pursuer 0) with PhaseIndex 0 and Engaged; boss positioned by setting Distance each step where stated)

```csharp
[Fact] public void Launch_is_refused_closer_than_60_m_or_beyond_160_m() { /* boss held at +50, then +170 for 10 s → no EnemyMissileLaunched; at +100 → one */ }
[Fact] public void Every_missile_is_announced_at_least_2_s_before_it_arrives() { /* real BossDriver, 90 s; player speed script cycles 20 → 45 → 69 → hard brake to 0 every 5 s, lane changes every 3 s; ≥ 3 launches; for each, Ended.Time − Launched.Time ≥ 2.0 − 1e-9 */ }
[Fact] public void Staying_in_lane_takes_25() { /* → Armor 75, Ended(Hit), "MISSILE HIT" */ }
[Fact] public void Lane_change_in_the_last_second_dodges() { /* player 30 m/s; hold SteerDir +1 from the step Locked turns true → Ended(Dodged), Armor 100, "MISSILE DODGED" */ }
[Fact] public void Early_lane_change_is_followed_and_hits() { /* change lane right after launch, then straight → Ended(Hit) */ }
[Fact] public void Guns_shoot_it_down_for_50() { /* missile 80 m ahead, Lateral == Player.Lateral (dead centre, between the two gun lines), Fire held → Ended(ShotDown), Score +50, "MISSILE SHOT DOWN +50" */ }
[Fact] public void Closing_speed_is_25_even_while_steering_hard() { /* player 20 m/s weaving (SteerDir +1 / −1 every 0.5 s, nose up to 20°); an EnemyMissile placed directly 60 m behind (Dir −1) → EnemyMissileEnded at (60 − 2.7)/25 = 2.292 s ± one step after placement */ }
[Fact] public void Respawn_clears_missiles_and_no_launch_while_wrecked() { /* MissionRun with the MissileBoss in range; player Wrecked → no EnemyMissileLaunched while Respawning; a missile already in flight is gone after Respawn (EnemyMissiles empty) */ }
[Fact] public void Only_one_flies_at_a_time_and_at_most_every_6_s() { /* launches ≥ 6 s apart; never two in EnemyMissiles */ }
[Fact] public void Missile_from_behind_can_be_dodged_too() { /* boss at −100 → Dir −1; last-second lane change → Dodged */ }
[Fact] public void Proving_ground_is_deterministic_with_every_weapon_used() { /* MissionRun(proving-ground, seed 9), 120 s scripted: Autopilot-style steering, Fire held, actions DropRear/CycleRear/FireMissile/FireStrike at fixed steps; twice → equal (StateHash, State, Time, Score, SpareCars) */ }
[Fact] public void Proving_ground_is_the_same_however_the_frames_are_split() { /* same script at 1/120 s, at 1/60 s and at the uneven 1/240 + 3/240 split (copy Mission_run_is_the_same_however_the_frames_are_split) → equal (StateHash, State, Time, Score, SpareCars). Actions are scheduled at even step counts only (every split has a frame boundary there) and handed to run.Handle before the first Update that starts at or after that step; 1/75 s frames are not used, because their boundaries fall between steps and an action could land one step apart. */ }
// InterpolationTests.cs
[Fact] public void Enemy_missile_prev_fields_equal_their_values_at_the_start_of_the_latest_step() { /* launched missile: Prev == spawn values on the launch step; after each 1/75 s Advance, PrevDistance/PrevLateral equal the values recorded at the start of the last step ± 1e-12 */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~EnemyMissileTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: slow homing enemy missiles with a guaranteed warning`.

### Task 14: Warnings and announcements in Core

**Files:**
- Create: `Simulation/Warnings.cs`, `Simulation/Announcement.cs`
- Modify: `Simulation/World.cs` (`Announcement`, `Announce`, `WarningRaised` tracking), `World.Boss.cs`, `World.Arsenal.cs`, `GameEvent.cs`, `Missions/MissionDirector.cs`
- Test: `tests/NightPursuit.Core.Tests/WarningTests.cs`

**Interfaces:**
- Consumes: the warning rules in `src/NightPursuit.App/Rendering/Overlays.cs` `DrawWarnings` as they stand when this task starts (pursuer behind or alongside, blocker ahead, wreck ahead), plus `GunnerDriver.IsAiming`, `EnemyMissile.TimeToImpact` and the hostile hazards.
- Produces:
  - `enum WarningKind { MissileIncoming, PursuerAlongside, GunnerAiming, HazardAhead, WreckAhead, BlockerAhead, BossBehind, HostileBehind, PursuerBehind }`. The order of declaration is the priority.
  - `readonly record struct Warning(WarningKind Kind, string Text, int Side, bool ShowArrow)`. Side is −1 left, +1 right, or 0 for both edges.
  - `static class Warnings { public static IReadOnlyList<Warning> Collect(World w); }`: at most 4, highest priority first. It is the source of truth for what the HUD shows.
    - **Moved from Overlays:** the pursuer and wreck rules and texts, copied unchanged. Blocker ahead keeps its text and now gets `ShowArrow` with Side = sign(ΔLateral), or 0 in your lane (|ΔL| < 1, the same dead zone as the pursuer arrow) (Plan 1 open item).
    - **MissileIncoming:** "MISSILE INCOMING {AHEAD|BEHIND}{ LEFT| RIGHT|}  IMPACT {t:0.0} s". Side from ΔL (1 m dead zone); arrow always.
    - **GunnerAiming:** "GUNNER AIMING - LEAVE ITS LANE" while `IsAiming`, the gunner is within 120 m ahead and |ΔL| < 2.
    - **HazardAhead:** "OIL AHEAD" or "CALTROPS AHEAD" for a hostile hazard 0–150 m ahead with |ΔL| < 2.5 (oil) or < HalfWidth + 1 (caltrops).
    - **BossBehind:** "BOSS BEHIND {LEFT |RIGHT }{n} m", Driving, 3–120 m behind; arrow as for the pursuer.
    - **HostileBehind** (call 29, HUD-4): the nearest hostile of any other kind (not Pursuer, which keeps its own rule, and not the boss) that is Driving, 3–60 m behind and closing faster than 3 m/s (`a.Speed − Player.Speed > 3`): "{KIND} BEHIND {LEFT |RIGHT }{n} m"; arrow as for the pursuer.
  - `sealed record WarningRaised(double Time, WarningKind Kind, string Text) : GameEvent(Time)` (call 30, §7.6). At the end of each `StepOnce` the World calls `Warnings.Collect` and publishes one `WarningRaised` for each kind that was not in the previous step's list. It is not part of `StateHash`.
  - `sealed record Announcement(string Title, string Detail, double Start, double Until)`. `World.Announcement` (null when `Time ≥ Until`) and `void World.Announce(string title, string detail, double seconds)`.
  - Announcements:

    | When | Title | Detail | Seconds |
    |---|---|---|---|
    | `BossAppeared` | "BOSS: {NAME}" | "DESTROY IT TO FINISH THE MISSION" | 3 |
    | `BossPhaseChanged` | "PHASE {n} OF {m}: {BEHAVIOUR}" | the hint for the behaviour (below), plus "  -  FIRES MISSILES" when the phase does | 2.5 |
    | `BeginStrike` | "SATELLITE STRIKE" | "TARGETING {n} HOSTILES" | 1.5 |
    | `StrikeLanded` | "STRIKE: {n} DESTROYED" | "" | 1.5 |

    Hints by behaviour (shown for boss phases):
    - Runner: "IT WEAVES FROM LANE TO LANE - KEEP YOUR NOSE ON IT"
    - Blocker: "IT WILL BLOCK AND BRAKE-CHECK YOU"
    - Pursuer: "LET IT PASS, THEN SHOOT"
    - Gunner: "IT SHOOTS BACK - STAY OUT OF ITS LANE"
    - Dropper: "OIL AND CALTROPS AHEAD"
    - Armored: "BULLETS DO A QUARTER - USE MISSILES AND HAZARDS"

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Missile_warning_shows_every_step_from_launch_to_outcome() { /* EnemyMissile added 100 m ahead in the player's lane (Dir +1) → Collect has MissileIncoming with "IMPACT 3.9 s" ((100 − 2.7)/25); on every step until EnemyMissileEnded it is present and first */ }
[Fact] public void At_most_four_warnings_show_missile_first() { /* missile + pursuer 40 m behind + gunner aiming + wreck ahead + blocker ahead → Count 4, kinds in order [MissileIncoming, GunnerAiming, WreckAhead, BlockerAhead] (PursuerBehind, the lowest, is dropped) */ }
[Fact] public void Gunner_aiming_warns_while_in_your_lane() { /* Aim phase, same lane, +45 → present; player moved 5 m over → absent */ }
[Fact] public void Hostile_oil_or_caltrops_ahead_warn_by_name_and_own_ones_do_not() { /* hostile slick 80 m ahead in your lane → HazardAhead "OIL AHEAD"; replaced by hostile caltrops → "CALTROPS AHEAD"; the same two with Hostile = false → no HazardAhead; hostile slick 80 m ahead one lane over (ΔL 5) → none */ }
[Fact] public void Any_hostile_closing_from_behind_warns_with_distance_and_side() { /* Gunner at −40, 5 m right, 5 m/s faster than you → HostileBehind "GUNNER BEHIND RIGHT 40 m", Side +1, ShowArrow; the same at 2 m/s faster → none; at −70 → none; Runner at −20, 5 m left, 10 m/s faster → "RUNNER BEHIND LEFT 20 m", Side −1; Armored at −40, 3 m right, chasing at +8 → "ARMORED BEHIND RIGHT 40 m" */ }
[Fact] public void Boss_behind_warns_within_120_m() { /* boss at −60 dead behind, same speed → BossBehind "BOSS BEHIND 60 m", Side 0, ShowArrow; at −130 → none */ }
[Fact] public void Blocker_ahead_now_shows_an_arrow() { /* Blocker 60 m ahead, 1.5 m right → BlockerAhead "BLOCKER AHEAD", ShowArrow, Side +1; in your lane (ΔL 0.5) → Side 0 */ }
[Fact] public void Plan_1_warning_texts_are_unchanged() { /* pursuer behind, pursuer alongside, wreck ahead: texts match the strings copied from Overlays */ }
[Fact] public void A_new_warning_publishes_WarningRaised_once() { /* gunner enters Aim in your lane → exactly one WarningRaised(GunnerAiming, "GUNNER AIMING - LEAVE ITS LANE") on that step and none on the following steps while it stays; after it clears for a step and returns → a second one */ }
[Fact] public void Boss_appearance_and_phase_change_are_announced() { /* BossAppeared → Announcement "BOSS: TEST" until +3 s; phase change → "PHASE 2 OF 2: PURSUER" / "LET IT PASS, THEN SHOOT" */ }
[Fact] public void Strike_is_announced_while_targeting_and_after() { /* two hostiles in range; FireStrike → Announcement "SATELLITE STRIKE" / "TARGETING 2 HOSTILES" until +1.5 s; on landing → "STRIKE: 2 DESTROYED" for 1.5 s; 1.6 s later Announcement is null */ }
[Fact] public void No_event_goes_unshown() { /* proving-ground 120 s scripted run (Task 13 script): for every event except BossDamaged, PlayerDamaged and WarningRaised, a Message or Announcement is published in the same step; every PlayerDamaged sets Player.HitFlash > 0; every WarningRaised's Kind is in Warnings.Collect on that step */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WarningTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: warnings, WarningRaised events and announcements computed in Core`.

### Task 15: Drawing the arsenal: smoke, caltrops, crates, missiles, strike

**Files:**
- Create: `src/NightPursuit.App/Rendering/ArsenalRenderer.cs`
- Modify: `Rendering/Neon.cs`, `Projector.cs`, `EffectList.cs`, `EffectRenderer.cs`, `HazardRenderer.cs` (hostile slick outline: red and dashed, so colour is not the only cue), `GameView.cs`, `Diagnostics/DiagnosticsRunner.cs`

**Interfaces:**
- Consumes:
  - from Core: `SmokeCloud`, `Caltrops`, `SupplyCrate.Label`, `Missile`, `EnemyMissile`, `Tracer.Hostile`, `SatelliteStrike.Targeting/SweepLeft`, `World.InStrikeRange`, `World.FindMissileTarget`, `KillCauseRules.LeavesWreck`
  - from the App: `Projector.ToScreen/ClipAt/Ppm/Lerp`, `LineBatch`, `HudLayout.Text`
- Produces:
  - `Projector`: `double Ahead(Missile m)`, `double Ahead(EnemyMissile m)`, `double Ahead(SmokeCloud s)` (interpolated, like `Ahead(Wreck)`), plus `double LateralOf(Missile m)`, `double HeadingOf(Missile m)` and `double LateralOf(EnemyMissile m)` (Lerp of the Prev fields). Every renderer in Tasks 15–18, the mirror and radar included, places these through them, never from raw `Distance`/`Lateral` (D18).
  - The strike's scan line is placed from `SweepLeft − w.Alpha·World.Step` (clamped at 0), so it moves evenly at 60 or 75 Hz instead of in one- and two-step jumps.
  - `Neon`:
    - colours `OrangeRed #FF6A3D` (Gunner), `Teal #2EE6C8` (Dropper), `SteelBlue #6F9BFF` (Armored), `Gold #FFD24A` (boss trim), `Steel #D0D8E0` (caltrops)
    - pens: `GunnerCar` (OrangeRed), `DropperCar` (Teal), `ArmoredCar` (SteelBlue), `BossCar` (White), `BossTrim` (Gold), `BossFlash` (Red), `EnemyTracer` (#FF5A36), `PlayerMissile` (White), `EnemyMissile` (Red), `Crate` (White), `Caltrop` (Steel), `StrikeBeam` (#9FF6FF), `HostileOutline` (Red, 0.8, dashed)
    - brushes: `SmokeFill` (Gray at 0.22), `CrateFill` (Cyan at 0.12)
  - `static class ArsenalRenderer`, each with `(DrawingContext dc, Projector p, World w)`, clipped by `ClipAt` like the other renderers:
    - `DrawSmoke`: a cloud of 10 soft grey ellipses spread over its 30 × 12 m footprint, 1–3 m tall, grown over the first 0.8 s and faded over the last 1 s
    - `DrawCaltrops`: a row of small steel stars across the lane, with a red dashed outline when hostile (shape as well as colour, NFR-3)
    - `DrawCrates(dc, p, w, HudLayout l)`: a 1.6 m glowing box drawn as a 3D outline, with its `Label` above it within 250 m
    - `DrawMissiles`: player missiles as a 2 m white dart with a flickering flame tail; enemy missiles as a red dart with a pulsing ring
    - `DrawStrike`: while targeting, a cyan scan line crossing the road, moving from 400 m toward the car as the sweep runs, and corner brackets on every car `InStrikeRange`
    - `DrawLock(dc, p, w)`: corner brackets plus "LOCK" on `FindMissileTarget()` while `Loadout.Missiles.Charges > 0`
  - `EffectRenderer.DrawTracers` uses `Neon.EnemyTracer` for hostile tracers.
  - `EffectList.Absorb` places explosions using `Cause.LeavesWreck()` in place of `== KillCause.Gunfire`. It adds `IReadOnlyList<Beam> Beams` (`Beam(double Distance, double Lateral)` with `Age`, `const double Life = 0.8`) for Satellite kills, aged in `Update`. `EffectRenderer.DrawBeams` draws a vertical glow from the road up to the top of the screen.
  - `GameView` order: slicks → caltrops → crates → vehicles and wrecks → smoke (cars inside smoke fade behind it) → wreck smoke → tracers → missiles → strike → beams → explosions → lock.
  - `DiagnosticsRunner`:
    - `--mission <name>` (default `mission-01`; letters, digits and '-' only, otherwise exit 2 with a message)
    - `--scene` takes a comma-separated list; with more than one scene, `--snapshot out.png` writes `out-<scene>.png` for each in one process
    - new scene **`arsenal`**: own smoke 30 m behind (seen in the mirror) and a second own cloud 60 m ahead (as after braking behind your own smoke; no enemy drops smoke in this plan); hostile caltrops 45 m ahead in your lane; crates Missile ×1 at 35 m and Armor ×1 at 80 m; a player missile 25 m ahead locked on a Runner 140 m ahead; an enemy missile 70 m ahead in the next lane; a hostile tracer 30 m ahead
    - new scene **`strike`**: three Runners at 60, 180 and 380 m, the strike half-way through its sweep, and one beam from a prior kill

- [ ] **Step 1: Implement and build** with `dotnet build src/NightPursuit.App -c Release`. Expected: succeeded with no warnings in new files.
- [ ] **Step 2: Run** `NightPursuit.App.exe --snapshot snapshots/p2-t15.png --seconds 6 --scene arsenal,strike`, then read both PNGs. Expected:
  - the smoke reads as a translucent grey cloud and the hostile caltrops have a red dashed outline
  - both crate labels are readable; the missile darts and the enemy-tracer colour are distinct
  - the LOCK brackets surround the Runner
  - the strike scan line and its brackets are visible, and the beam reaches the top of the screen
  - nothing is drawn through car bodies or above hill crests
- [ ] **Step 3: Commit** `feat: draw smoke, caltrops, crates, missiles and the satellite strike`.

### Task 16: Drawing the new enemies and the boss: view, mirror and radar

**Files:**
- Modify: `Rendering/VehicleRenderer.cs`, `Mirror.cs`, `Radar.cs`, `EffectList.cs` (boss explosion), `Diagnostics/DiagnosticsRunner.cs`

**Interfaces:**
- Consumes: `AgentKind.Gunner/Dropper/Armored/Boss`, `GunnerDriver.IsAiming`, `DropperDriver.IsArming`, `BossCar.Shield/ArmorOpen/Definition.Name`, `AgentCar.Punctured`, the Neon pens from Task 15.
- Produces:
  - `VehicleRenderer.Body` gets an outline per kind (Plan 1 keeps the sedan, box truck and spoiler car):
    - **Gunner:** an orange-red fastback with a sloping roof and a square gun port in the rear window, glowing while aiming and white during the burst
    - **Dropper:** a teal van with a tall flat hatch and a chute below the bumper (a down chevron), glowing while arming
    - **Armored:** a steel-blue wide wedge narrowing to a flat roof, with two plate lines, rivet dots and a narrow slit window
    - **Boss:** a white long low interceptor with two tall rear fins and a full-width tail light strip trimmed in gold, a central exhaust ring, plus a "BOSS" tag above it within 300 m. It flashes red when hit, has a pulsing cyan outline while `Shield > 0`, and shows a jagged "open armor" line while spinning.
    - A punctured car wobbles its tilt with its puncture wobble and shows a tyre spark under one wheel.
  - `Mirror`: colours by kind; a boss in the mirror is drawn 1.3× with its gold-trimmed tail strip. It also draws enemy missiles behind (a red dot with a ring) and smoke clouds behind (soft grey), placed with the Projector's `Ahead`/`LateralOf` (Task 15), like the cars it already draws.
  - `Radar` (HUD-3, colour plus shape):
    - Gunner: an orange-red ring with a centre dot
    - Dropper: a teal down-triangle
    - Armored: a steel-blue hexagon
    - Boss: a white double ring with a gold inner ring, pulsing
    - smoke: a grey rounded rectangle at its real footprint
    - caltrops: three steel dots
    - crates: a white square with its item letter (O S C M ★ A)
    - missiles: a white dot (yours) and a red dot with a ring (enemy)
  - `EffectList`: a boss kill spawns three staggered explosions over 0.6 s.
  - New diagnostics scenes:
    - **`enemies`**: a Gunner aiming at 30 m in your lane, a Dropper arming at 90 m, an Armored at 55 m in the next lane, a punctured Runner at 120 m, and an Armored 30 m behind (mirror and radar)
    - **`boss`**: the mission-01 boss 45 m ahead at 65% health in phase 1, spinning (open armour), with a 0.5 s Shield left so the cyan outline shows

- [ ] **Step 1: Implement and build.** Expected: succeeded.
- [ ] **Step 2: Run** `--snapshot snapshots/p2-t16.png --seconds 6 --scene enemies,boss`, then read both. Expected:
  - every kind is recognisable by its outline alone, and the gun port and chute glows are visible
  - the Gunner's orange-red reads differently from the Pursuer's red, and the Dropper's teal and the Armored's steel blue from the cyan road and blue lane lines (if not, say so in the task report; the shapes still carry the meaning)
  - the mirror shows the Armored behind; the radar shows each kind with its own shape and colour
  - the boss shows its fins, gold-trimmed tail strip, BOSS tag and open-armour line
- [ ] **Step 3: Commit** `feat: draw gunner, dropper, armored and boss in view, mirror and radar`.

### Task 17: Keys, help and the weapon panel

**Files:**
- Create: `src/NightPursuit.App/Rendering/WeaponPanel.cs`
- Modify: `Rendering/Hud.cs` (oil pips and gun heat move to the panel; car-state lines), `Overlays.cs` (help text), `GameView.cs`, `Input/InputHub.cs`, `Input/XInputPad.cs`, `App.xaml.cs` (`--mission`), `Diagnostics/DiagnosticsRunner.cs`

**Interfaces:**
- Consumes: `Loadout` (Tasks 1, 5–7), `MissionDirector.Stage/Boss` (Task 12), `PlayerCar.GripLoss/PunctureLeft` (Task 4), `ActionKind.CycleRear/FireMissile/FireStrike`.
- Produces:
  - **Keys (call 1):**
    - `InputHub.OnKeyDown`: `Key.Z`, `Key.LeftShift`, `Key.RightShift` → `CycleRear`; `Key.F` → `FireMissile`; `Key.V` → `FireStrike` (non-repeat only); all return true
    - `XInputPad`: `X = 0x4000` → `CycleRear`, `RightShoulder = 0x0200` → `FireMissile`, `LeftShoulder = 0x0100` → `FireStrike`
  - **Help overlay:**
    - WEAPONS line → "Space guns (hold)   X / Ctrl drop rear weapon   Z / Shift next rear weapon"
    - a new line → "F missile (locks on ahead)   V satellite strike"
    - gamepad line → "A guns   B drop   X next rear   RB missile   LB strike   Y cruise …"
  - **`static class WeaponPanel { static void Draw(DrawingContext dc, HudLayout l, World w); static Rect Bounds(HudLayout l); }`:** left edge, from 150k down, 250k wide.
    - GUNS: heat bar plus "GUN HEAT" or "GUNS LOCKED" (moved from Hud).
    - REAR: three rows (OIL, SMOKE, CALTROPS), each with charge pips against its capacity. The oil row has a thin recharge bar from `RechargeProgress`. The selected row has a bright box and a "▶" marker. An empty row reads "EMPTY", a row in cooldown reads "WAIT". A caption reads "X drop   Z next".
    - MISSILES: pips plus "F". The line under it reads "LOCK: {KIND} {n} m" (green) or "NO TARGET IN SIGHT" (grey).
    - SAT STRIKE: "V  READY", "NONE", or "TARGETING {t:0.0} s" with a bar.
  - **`Hud.DrawCarState`:**
    - under the armor bar it shows "NO GRIP" (while GripLoss > 0) and "PUNCTURED {t:0.0} s" with a shrinking bar
    - in the Boss stage the line "DISTANCE TO GO" becomes "BOSS: {NAME}"
    - `ReadoutBounds` shrinks to the top-right block as it now is
  - **`App.OnStartup`:** `--mission <name>` picks the mission file. A bad name or missing file shows the existing readable error window.
  - New diagnostics scene **`weapons-hud`**: Caltrops selected, smoke empty, oil recharging at 2 of 5, missiles 2 with a lock, strike targeting, and the player punctured.

- [ ] **Step 1: Implement and build** Release and Debug. Expected: both succeed.
- [ ] **Step 2: Run** `--snapshot snapshots/p2-t17.png --seconds 6 --scene weapons-hud,close-cars`, then read both. Expected:
  - every panel readout above is present and readable at 1280×720, including "WAIT", "EMPTY", the lock line and "PUNCTURED"
  - close-cars still looks as in Plan 1, apart from the weapon panel
- [ ] **Step 3: Check by reading the code** that every new `ActionKind` reaches `MissionRun.Handle` through `GameLoop` unchanged, and that the help text lists every new key.
- [ ] **Step 4: Commit** `feat: new weapon keys, help text and the weapon panel`.

### Task 18: Boss bar, warnings and announcements on the HUD

**Files:**
- Create: `src/NightPursuit.App/Rendering/BossBar.cs`
- Modify: `Rendering/Overlays.cs` (draws `Warnings.Collect` and announcements), `GameView.cs`, `Diagnostics/DiagnosticsRunner.cs`

**Interfaces:**
- Consumes: `Warnings.Collect`, `World.Announcement` (Task 14), `MissionDirector.Stage/Boss` (Task 12), `BossCar` (Task 10), `BossDamaged`.
- Produces:
  - **`static class BossBar { static void Draw(DrawingContext dc, HudLayout l, MissionRun run); const double Height = 34; }`:** drawn in the Boss stage while the boss isn't destroyed, centred under the mirror at y 100k, 520k wide.
    - name and "PHASE n/m: BEHAVIOUR" above the bar
    - a health fill with tick marks at each phase threshold (60% and 25% for the Black Heron), and the percentage as a number
    - a red flash for 0.15 s after `BossDamaged`
    - "SHIELDED" while Shield > 0, "ARMOR OPEN ×2" while spinning, "CLOSING IN" while not Engaged
  - **`Overlays.DrawWarnings(dc, l, w, double top)`:**
    - draws `Warnings.Collect(w)` as pulsing centred lines 24k apart from `top`, coloured red for MissileIncoming, PursuerAlongside and GunnerAiming, amber for the rest
    - an edge arrow for each warning with `ShowArrow`
    - `top` = 108k, or 108k + `BossBar.Height`·k when the bar shows; messages start below the last warning row
  - **`Overlays.DrawAnnouncement(dc, l, w)`:** only while Playing, at 0.40·height. A dark backing box holds the Title (34) and the Detail (18), fading in over 0.2 s and out over the last 0.4 s.
  - New diagnostics scene **`missile-warning`**: the boss in a missile phase 100 m ahead, a missile in flight 1.6 s from impact in the next lane, a hostile oil slick 90 m ahead in your lane, a Gunner 30 m behind closing at +6 m/s, and a phase announcement active.

- [ ] **Step 1: Implement and build** Release and Debug. Expected: both succeed.
- [ ] **Step 2: Run** `--snapshot snapshots/p2-t18.png --seconds 6 --scene missile-warning,boss`, then read both. Expected:
  - "MISSILE INCOMING … IMPACT 1.6 s" is the top warning with its edge arrow; "OIL AHEAD" and "GUNNER BEHIND … 30 m" with its arrow are below it
  - the warnings don't overlap the boss bar, mirror or messages
  - the boss bar shows the name, phase, tick marks, percentage and "SHIELDED"/"ARMOR OPEN ×2"
  - the announcement box is legible and doesn't cover the aim mark
- [ ] **Step 3: Commit** `feat: boss bar, Core warnings and announcements on the HUD`.

### Task 19: Stress, performance, decisions log and the owner's checks

**Files:**
- Modify: `Diagnostics/DiagnosticsRunner.cs` (`PlaceStressFrame`, `PlacePerfScene`), `docs/decisions.md`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - **`--stress`** (NFR-5) now cycles every kind with `(AgentKind)((k + frame) % 6)`. Every third frame it adds a boss 60 m ahead, varying phase, shield, spin and Engaged. It also places per frame:
    - 2 smoke clouds, 3 caltrops (half hostile) and 3 crates
    - 2 player missiles and 1 enemy missile
    - a punctured car and an aiming gunner
    - a strike sweep on odd frames, an announcement on frames divisible by 5, and GripLoss/PunctureLeft alternating

    It runs on mission-01 and on proving-ground at the same three sizes plus 0×0.
  - **`--perf`:** the close-cars scene plus 25 cars, now cycling all six kinds, plus the boss, 3 smoke clouds, 4 crates, 2 missiles and an active strike.
  - **`docs/decisions.md`:** a new section "Plan 2 calls (before building)" logging calls 1–30 of this plan as D22–D51 (D21 is already used for the fixed open items), in the owner's plain style: what, why, cost to change. Add a "Checks only you can do" list for Plan 2.

- [ ] **Step 1: Implement and build** Debug and Release.
- [ ] **Step 2: Run** `--stress snapshots/p2-stress.txt`. Expected: `0 failures` at every size for both missions.
- [ ] **Step 3: Run** `--perf snapshots/p2-perf.txt`. Expected: mean `OnRender` < 8 ms. If it's slower, batch the smoke ellipses and crate outlines into shared `StreamGeometry` per pen before continuing.
- [ ] **Step 4: Run the whole Core suite.** Expected: all pass.
- [ ] **Step 5: Write the decisions-log section**, including the owner's checks (deferred, D6):
  - play mission 1 to the Black Heron and beat all three phases; say whether the slow 300 m arrival and the pull-ahead Pursuer phase feel right
  - on `--mission proving-ground`: smoke a pursuer and watch it wander in the mirror; caltrop a runner; lock and fire a missile; use the strike; dodge a boss missile in the last second and shoot one down
  - check that every warning and banner is readable without sound, and that the new enemy colours are easy to tell apart
- [ ] **Step 6: Commit** `feat: stress and perf cover the full arsenal and bosses; decisions D22-D51`.

---

## Spec coverage for this plan

| Spec | Where |
|---|---|
| WPN-2 (recharge shown on HUD: Plan 1 open item) | Tasks 1, 17 |
| WPN-3 smoke screen (lose sight, wander, slow, collide) | Tasks 1, 2, 3, 15 |
| WPN-4 caltrops (one lane, puncture, slow, wobble, stop off the road) | Tasks 1, 4, 15 |
| WPN-5 missile (lock nearest hostile ahead in range and view; destroys; boss 20%) | Tasks 5, 11, 15 |
| WPN-6 satellite strike (1.5 s sweep, 400 m ahead / 150 m behind, hostile only, wrecks) | Tasks 6, 15 |
| WPN-7 ramming against the boss (never off the road) | Tasks 10, 11 |
| WPN-8 supply crates with labels; per-mission loadout | Tasks 7, 12, 15 |
| All weapons have readouts (ammo/charges, cooldown) | Task 17 |
| WRK-1 (missile and strike wrecks), WRK-2 (puncture crash beside the road) | Tasks 4, 5, 6 |
| ENM-1 colour per type, on radar, plus shape (NFR-3) | Tasks 15, 16 |
| ENM-2 a driver class per behaviour; boss phases reuse them | Tasks 8, 9, 10 |
| Enemy table: Gunner, Dropper, Armored, Boss with 2–3 phases | Tasks 8, 9, 10, 12 |
| ENM-3 boss damage table and phase thresholds | Tasks 10, 11 |
| ENM-4 enemy missiles: banner and side arrow ≥ 2 s, dodge in the last second, shoot down | Tasks 13, 14, 18 |
| MSN-2 the boss appears after the approach; destroying it completes the mission | Task 12 |
| MSN-3 respawn keeps the boss's health | Task 12 |
| MSN-4 boss, crates and loadout in the mission files | Tasks 7, 12 |
| HUD-1 weapon readouts, boss health bar | Tasks 17, 18 |
| HUD-3 radar: slicks, caltrops, crates, every hostile type | Task 16 |
| HUD-4 warnings with side arrows (missile, blocker ahead, any hostile closing from behind), messages for every event | Tasks 14, 18 |
| §7.2 collisions for every pair, broad phase sorted by distance; WorldView hides the player in smoke | Tasks 2, 3 |
| §7.3 weapon hierarchy; enemies reuse MachineGuns, OilDropper, CaltropDropper | Tasks 1, 8, 9 |
| §7.6 GameEvents for every notable thing, warnings included (`WarningRaised`) | Tasks 1–14 |
| §8.3 default keys F, V, Z/Shift | Task 17 |
| §8.4 diagnostics (`--mission`, new scenes, stress, perf) | Tasks 15–19 |
| NFR-1 perf, NFR-2 determinism and frame splits, NFR-3 on-screen only, NFR-5 stress | Tasks 3, 13, 14, 19 |

**Deferred:**
- Plan 3: "satellite strike spares civilians and police" as a scenario test (the code already filters on `Faction.Hostile`), civilian and police reactions to smoke, and heat from weapons.
- Plan 4: missions 2–6 (and so missiles in missions 5–6), the briefing screen with the boss drawing and loadout (MSN-1), the mission bonus (MSN-5), respawning "a short way back" (MSN-3, a Plan 1 open item), and the text-size setting.
