# Night Pursuit Plan 3: Traffic, Police and Heat. Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the road with civilian traffic that reacts to danger, add a heat meter that rises when you hurt civilians or
police, and bring in police who escalate with it. At Patrol level they set speed traps and roadblocks. At Pursuit they
chase and box you in. At Manhunt they ram and form rolling roadblocks. If they hold you still for 3 s, you are busted.
Everything shows on screen.

**Architecture:** Civilians and police are `AgentCar`s with new `AgentKind`s appended after `Boss`, and
`Faction` now comes from the kind (`Civilian`, `Police`), so physics, collisions, keep-clear, mirror and radar all work
unchanged. The faction is checked wherever "hostile" matters: scoring, warnings, missile, strike and smoke.

New Core folders:
- `Police/`: `HeatMeter`, `HeatRules` (the HT-1/HT-2 table as data), `PoliceDispatcher`, `PoliceLanes` (the
  three-lane guarantee) and `BustedWatch`.
- `Traffic/`: `TrafficDirector`.

The new drivers go under `Drivers/`, and two roadside hazards (`SpeedTrap`, `Roadblock`) under `Hazards/`. Offences go
through one `World.Heat.cs` partial, so there is one place where heat and blame meet. Spawning uses a
separate `TrafficRng`, so missions without traffic give exactly the same seeded results as before. The App gets new
car shapes and light bars, roadside drawing, a heat meter and a busted countdown, plus new `--scene` snapshots.

**Tech Stack:** C# / .NET 10, WPF (`net10.0-windows`), xUnit 2.9.3, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-11-17-night-pursuit-design.md`: §5.5 CIV-1–3, §5.6 HT-1–7, §5.8 HUD-1/3/4, §7.2,
§7.4, §7.6 and §11 ("three civilian hits reach Patrol level", "satellite strike spares civilians", busted countdown and
cancel, roadblock gap guarantee, police escalation). Also read `docs/decisions.md`:
- D12 (focus pause)
- D15 (per-step mission rules)
- D18 (interpolated drawing)
- D22 (HUD layout)
- D27 (smoke)
- D54 (AI-vs-AI kills score)
- D58/D61 (respawn clears)
- D66 (help box)
- D67 (boss cues)
- D68 (Tab)

**The four plans:**
1. Foundation and playable slice (built, branch `plan-1-foundation`)
2. Full arsenal, all six enemy types and the boss (built, branch `plan-2-arsenal-and-bosses`)
3. **Civilians, police, heat and busted (CIV, HT)** (this plan)
4. Six missions, all screens, settings, high scores, persistence (MSN, HUD-5, NFR-4)

## Global Constraints

- Target frameworks: Core and Tests `net10.0`; App `net10.0-windows` with `<UseWPF>true</UseWPF>`. `Directory.Build.props` is unchanged.
- Core references no WPF types and never calls `DateTime.Now`, `Stopwatch` or `new Random()`. Time arrives as `dt`. Randomness comes from `World.Rng` / `WorldView.Rng`, or for spawning only from the new `World.TrafficRng`.
- **A mission without `traffic` and `police` sections must give bit-for-bit the same results as before this plan.** No new draws from `World.Rng` happen unless civilians or police exist. Every existing test keeps passing unchanged, including the determinism and frame-split tests.
- Units inside Core: metres, seconds, m/s, radians. Only the HUD shows mph (`Units.MphPerMs = 2.23694`). 5 mph = 2.235 m/s; 70 mph = 31.29 m/s.
- Simulation step is exactly `World.Step = 1.0 / 120` s. Rules that decide the mission (heat, busted, spawning) run once per step (D15).
- Everything that moves has `Prev…` fields, is refreshed in `World.CapturePrev`, and is drawn through the Projector (D18). Anything spawned sets `Prev…` to its spawn values (`SnapPrev()`).
- **Nothing important may depend on sound.** Every heat change, offence, police escalation, speed trap, roadblock and busted countdown shows words on screen. Colour is never the only cue:
  - civilians have their own shapes;
  - police have a light bar;
  - the heat meter shows the level name as text.
- Original work only: no names, cars, art or music from Spy Hunter, Speed Freak or any other game.
- No screen-capture scripts. Visual checks use the app's `--snapshot`, `--stress` and `--perf` only. Launch the freshly built exe as few times as possible (antivirus): one `--scene a,b,c` run per task. If the permission system refuses a launch, do not retry; report what is left unseen.
- App exe: `src/NightPursuit.App/bin/Release/net10.0-windows/NightPursuit.exe`, built with `dotnet build src/NightPursuit.App -c Release`. Snapshots go in `snapshots/` (git-ignored).
- **Commits:** local commits on a new branch `plan-3-traffic-and-police` created from `plan-2-arsenal-and-bosses`. Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never push.
- Test command (from repo root): `dotnet test tests/NightPursuit.Core.Tests`.
- `WarningKind` must stay under 32 members (`World.PublishNewWarnings` keeps a 32-bit mask).

## Review Focus

1. **Boxed in with no way out.** Police and other cars fill all three lanes around you, or a roadblock's gap is blocked. Expected (HT-7):
   - moving police cars never occupy all three lanes within 40 m of you;
   - braking always lets you escape a box-in;
   - a roadblock whose gap lane is blocked by a wreck is cleared at once.

   *(Task 8: `Police_never_fill_all_three_lanes_around_you`, `Braking_breaks_a_box_in`; Task 6: `A_roadblock_whose_gap_is_blocked_is_cleared`.)*
2. **Busted unfairly.** You stop at a wreck or roadblock with a patrol car nearby, or a police car passes while you are slow, paused or respawning. Expected:
   - busting only happens at Pursuit or above, and the countdown is on screen from its first step;
   - speeding up above 5 mph cancels it;
   - nothing counts while paused, wrecked or respawning.

   *(Task 10: `Busted_needs_pursuit_level`, `Countdown_shows_from_the_first_step_and_cancels_above_5_mph`, `Nothing_counts_while_wrecked_or_respawning`.)*
3. **Heat runs away from one burst.** Holding the guns through a civilian, or spraying a police car. Expected:
   - a civilian adds at most +10 per second of hits, plus +25 once when destroyed;
   - heat stays within 0–100;
   - every change is published with a message.

   *(Task 3: `Holding_fire_on_a_civilian_adds_at_most_10_per_second`, `Heat_stays_between_0_and_100`.)*
4. **Traffic pops in, piles up or spawns into trouble.** Expected:
   - civilians never appear inside the drawn view (spawn at least 1250 m ahead, or at least 400 m behind);
   - never within 40 m of another car in the same lane;
   - never on a wreck or roadblock;
   - the number around you stays near the mission's density.

   *(Task 4: `Traffic_spawns_out_of_sight_and_spaced`, `Traffic_density_stays_near_the_target`.)*
5. **A civilian destroyed by an enemy counts against you.** Expected: heat applies only when you hurt that car in the last 5 s (your ram, bullets, oil or caltrops). Crashes that enemies cause are never your fault. *(Task 3: `Enemy_caused_civilian_crash_costs_nothing`, `Your_oil_crash_counts_against_you`.)*

## Calls made in this plan

The spec leaves these open. Each one is logged as a decision for the owner in Task 16 (from D69).

1. **Civilians and police are `AgentCar` kinds, not new classes.** `AgentKind` gains `Civilian, PolicePatrol, PoliceCruiser, Interceptor, ArmoredSuv`, appended after `Boss`. `Faction` gains `Civilian` and `Police`, and `AgentCar.Faction` comes from the kind. This reuses all the car rules, and spec §13 leaves class detail to the builder.
2. **No point penalties for civilians** (the owner's ruling, 2026-11-19). Hurting a civilian costs you armor (the normal contact damage) and raises heat, but never costs points. Civilians and police are worth no points either. The owner will judge the police in play and may retune the heat levels.
3. **Blame window.** A civilian or police car counts against you only if **you** hurt it in the last **5 s**: your ram, your bullets, or your own oil or caltrops. A crash caused by an enemy or by another car is not your fault.
   - A contact is **your** fault only when **you** run into the other car: you rear-end it, or you hit its side while moving into it faster than it moves into you (`playerInto > agentInto`).
   - A civilian or police car that rear-ends you, swerves into you or rams you costs you nothing. An interceptor ramming you gives no heat.
4. **How often a "hit" counts.**
   - **Civilians:** a hit (bullet or contact) costs **+10 heat at most once per car per second**.
   - **Police contact** (+15 heat) counts at most once per car per second.
   - **Police bullets** cost **+5 heat per hit**, as the spec says.
5. **Smoke:**
   - **Civilians are not blinded by smoke.** Inside a cloud they slow to 60% of their cruise speed and keep their lane.
   - **Police are blinded exactly like enemies** (D27). That makes smoke a real way to shake a pursuit, and a blinded police car can't bust you.
6. **The satellite strike and missiles spare civilians and police.** This is already true of the code. This plan adds the spec §11 scenario tests.
7. **Traffic:**
   - same direction only, at 25–35 m/s, in lanes;
   - spawned **1250–1450 m ahead**, beyond the 1200 m drawing distance, so cars never pop in;
   - also **400–450 m behind** while you drive slower than 25 m/s;
   - culled by the existing 1500 m / 500 m window.
   - The mission key is `"traffic": { "carsPerKm": n }` (0–12, default 0). Mission 1 gets 3.
8. **Police settings:** `"police": { "presence": p, "startingHeat": h }`.
   - `presence` runs from 0 to 2. 0, the default, means no police at all. 1 is normal.
   - `startingHeat` runs from 0 to 100, default 0.
   - Mission 1 gets presence 1. A new test mission, `patrol-ground` (call 21), starts at heat 75.
9. **Heat decay:**
   - −2 per second after 5 s without an offence;
   - **−4 per second** when no police car is "near" (spec: "faster").
   - Heat is clamped to 0–100.
   - **"Near"** means a police car within 300 m, **or** any police car that is chasing you and can see you (not blinded), however far away it is.
   - **Levels have hysteresis.**
     - A level is entered at its threshold (25 / 50 / 70 / 90).
     - It is left only when heat falls **5 below** that threshold (20 / 45 / 65 / 85).
     - Each police level (Patrol and above) is held for at least **20 s** after it was reached.
     - Without this, Pursuit at 75 would end after about 6 s, long before the cruisers could arrive.
   - The spec's §11 example, "three civilian hits reach Patrol level", only works from a starting heat of 20 or more, because three hits of +10 from 0 give 30, which is Watched. The HT-2 numbers are kept as they are, and this is logged for the owner.
10. **Speed traps:**
    - a roadside camera post with its posted limit, chosen from **60, 70 or 80 mph**;
    - the first one 700 m ahead as soon as Patrol is reached, then every **1500 m** while heat is at Patrol or above;
    - passing it above the limit gives **+8 heat once**;
    - its sign and a "SPEED TRAP" warning show from **400 m** (spec: at least 300).
11. **Roadblocks:**
    - barriers across **two lanes**, always leaving **one lane open**, marked with green chevrons and a "ROADBLOCK - GAP LEFT/CENTRE/RIGHT" warning with an arrow from **400 m**;
    - the first one 800 m ahead as soon as Patrol is reached, then every **3000 m** at Patrol or above, one at a time;
    - never within 150 m of a wreck; never during the boss stage, and any roadblock left on the road is removed when the boss stage starts;
    - hitting one does **25 damage**, drops you to 5 m/s, and gives **+15 heat once**; you crab along it at 5 m/s (3 m/s for a wreck), so you are past it well within the 3 s busted countdown;
    - other cars treat it like a wreck and see it from `max(120 m, their speed × 5 s)`, so they have time to reach the gap;
    - if a wreck ever lands in its gap lane from 150 m before it to 30 m after it, the roadblock is cleared ("ROADBLOCK CLEARED");
    - within 150 m before a roadblock, chasing police drop back behind you, so a police car is never stopped in the gap.
12. **Patrol level:**
    - about **2 × presence** patrol cars drive in traffic within the window (28–32 m/s) and ignore you;
    - their light bars still flash (HT-4).
13. **Pursuit level:**
    - up to **round(2 × presence)** cruisers, at most 3, chase you; they spawn **350–450 m behind** you, at your speed + 15, and close at up to 20 m/s;
    - the nearest two take box-in roles: **Front** gets ahead in your lane, about 12 m in front, then slows to your speed − 4, never below 8 m/s; **Side** holds beside you in a neighbour lane, never slower than 3 m/s, so it rolls on if you stop;
    - the rest are **Trail**: in your own lane, about 20 m behind, stopping 6 m behind you if you stop;
    - they never ram at Pursuit;
    - patrol cars within 600 m switch to chasing.
14. **Manhunt level:**
    - **interceptors** (top speed 80 m/s; you top out near 67) ram you like pursuers, one per presence, at most 2;
    - **armored SUVs** (armor 30, top speed 82 m/s) form a **rolling roadblock**. They spawn 350–450 m behind and overtake you on the side with room. Once both are at least 60 m ahead, they form up side by side in two adjacent lanes about 40 m ahead and **lock the open lane**: the lane farthest from yours, or a random outer lane if you are in the middle. While you are within 40 m they ride at your speed − 6. They need presence of at least 1.
    - The rolling roadblock gets the same "GAP LEFT/CENTRE/RIGHT" warning with an arrow, and its own radar mark, as a parked one.
    - While the SUV pair is within 80 m of you, the Side role stands down to Trail, so the SUVs and police together never fill three lanes.
    - The police cap within the window is 6, filled in priority order: SUVs, interceptors, cruisers, then patrols. Patrol cars over the cap are removed only when out of sight, more than 1200 m ahead or more than 300 m behind.
15. **When heat drops a level,** chasing police switch back to patrol driving at 25 m/s and fall away behind you, with the message "POLICE BACKING OFF".
16. **Busted (HT-6):**
    - it can only happen at **Pursuit or above**;
    - you must be below 5 mph with a non-blinded police car within **15 m** (straight-line road distance between centres) for **3 s**;
    - "BUSTED IN 3 / 2 / 1" shows large on screen from the first step, with "SPEED UP TO BREAK FREE" under it;
    - going above 5 mph cancels it;
    - a bust costs a spare car through the usual respawn;
    - after a bust you respawn with heat set to **50** (bottom of Patrol), so you aren't busted again at once. The change is shown ("HEAT SET TO 50: PATROL"). A wreck leaves heat as it was.
17. **The three-lane guarantee (HT-7)** is enforced in one helper, `PoliceLanes`, **every step for every police car**, not only when it changes lane:
    - a lane counts as covered by a police car when the car's body overlaps it (`|lateral − lane centre| < 2.5 + width/2`), so a car halfway between lanes covers both;
    - if the moving police within 60 m of you would cover all three lanes, the car that would complete the third lane is held back: it keeps more than 45 m from you (a speed cap) until the lanes free up;
    - patrol cars just driving in traffic count too.
18. **Colours and shapes:**
    - **Civilians:** dim neutral grey (`#7F8C99`), with three body shapes chosen by car id (sedan, hatchback, van).
    - **Police:** a pale silver-blue body (`#C9D6EA`) with a **light bar** alternating blue `#2E6BFF` and red `#FF2E4D` every 0.2 s. Interceptors are low and long; armored SUVs are tall and boxy.
    - **Radar:** civilians are small grey dots; police are squares flashing blue and red (bigger for an SUV); a speed trap is an amber "S" at the road edge; a roadblock is a red bar with a green gap tick.
19. **The heat meter** sits in the right column under the score and armor block, so `Hud.ReadoutBounds` grows from 156 to 200 HUD units.
    - It shows "HEAT", a 0–100 bar in five coloured segments, the level name in its colour, and the number.
    - It flashes for 1 s whenever heat rises, blinking at 2 Hz.
    - Each level rise shows a banner: "POLICE PATROLS" / "POLICE PURSUIT" / "MANHUNT", each with a one-line hint. Every level change **also** publishes a Message, because a banner can be held back while the boss's urgent banner is showing.
    - Level colours: Calm green `#39FF88`, Watched yellow `#FFE14D`, Patrol amber `#FFC13B`, Pursuit orange `#FF8A1F`, Manhunt red `#FF3B3B`.
20. **Every offence shows a message with its cost**, for example "CIVILIAN HIT  +10 HEAT", "POLICE CAR HIT  +15 HEAT" or "SPEED TRAP 112 IN A 70  +8 HEAT".
21. **A test mission, `content/missions/patrol-ground.json`:**
    - heavy traffic (carsPerKm 6), police presence 1.5, starting heat 75;
    - two runner waves, a crate of each weapon, and a 2-phase boss.
    - It opens with `--mission patrol-ground`, so the owner can meet every police level.
22. **New events for later sound** (§7.6): `HeatChanged`, `HeatLevelChanged`, `PlayerBusted`, `SpeedTrapPassed`, `RoadblockHit`. `VehicleDestroyed` gains a trailing `Faction`.
23. **Warnings** (HUD-4):
    - new kinds `PoliceAlongside` ("POLICE LEFT/RIGHT" with an arrow), `RoadblockAhead` (parked or rolling), `PoliceBehind` ("POLICE BEHIND n m") and `SpeedTrapAhead`;
    - police warnings only at Pursuit or above;
    - **civilians never raise warnings**;
    - the hostile-behind rule only counts `Faction.Hostile`.
24. **Enemies and police ignore each other.** Enemy bullets still never hit cars. Collisions between them follow the car-to-car rules, with no heat and no blame.
25. **Traffic and police see you ahead of them.** The existing keep-clear rule ignores the player, so civilians and police would plough into you whenever you are slow: after a roadblock or wreck, after a respawn, or while boxed in. Instead, they treat you as a car ahead: they change to a free lane, or follow at your speed − 2.
26. **Missions can't misuse the new kinds.** A wave kind or a boss behaviour must be one of the six enemy types. "Civilian" or any police kind is rejected with a clear message.
27. **Counts use whole-number rounding away from zero** (`MidpointRounding.AwayFromZero`), so presence 1.5 gives 3 patrol cars, not 2.

---

## File structure

```
content/missions/mission-01.json               (modified: traffic, police)
content/missions/patrol-ground.json            (new)
src/NightPursuit.Core/
  Police/    HeatLevel.cs, HeatRules.cs, HeatMeter.cs, BustedWatch.cs, PoliceDispatcher.cs, PoliceLanes.cs
  Traffic/   TrafficDirector.cs
  Drivers/   CivilianDriver.cs, PatrolDriver.cs, ChaseDriver.cs, InterceptorDriver.cs, RollingRoadblockDriver.cs
             (modified: DriverHelpers.cs, WorldView.cs)
  Hazards/   ILaneBlocker.cs, SpeedTrap.cs, Roadblock.cs   (modified: Wreck.cs)
  Vehicles/  (modified: Vehicle.cs Faction, AgentCar.cs, VehicleStats.cs)
  Simulation/ World.Heat.cs   (modified: World.cs, World.Arsenal.cs, Collisions.cs, HazardContacts.cs, GameEvent.cs, Warnings.cs)
  Missions/  TrafficDefinition.cs, PoliceDefinition.cs   (modified: MissionDefinition.cs, MissionLoader.cs, MissionDirector.cs, MissionRun.cs)
src/NightPursuit.App/
  Rendering/ HeatMeterView.cs, RoadsideRenderer.cs
             (modified: Neon.cs, VehicleRenderer.cs, Mirror.cs, Radar.cs, Hud.cs, Overlays.cs, GameView.cs)
  Diagnostics/ (modified: DiagnosticsRunner.cs)
tests/NightPursuit.Core.Tests/
  FactionTests.cs, HeatTests.cs, OffenceTests.cs, TrafficTests.cs, CivilianTests.cs, RoadsideTests.cs,
  PoliceTests.cs, BustedTests.cs   (modified: WarningTests.cs, MissionTests.cs, WorldLoopTests.cs)
docs/decisions.md                              (modified: D69 onward, open items, owner's checks)
```

---

### Task 1: Factions, the new kinds and plain lane driving

**Files:**
- Modify: `Vehicles/Vehicle.cs`, `Vehicles/AgentCar.cs`, `Vehicles/VehicleStats.cs`, `Drivers/DriverHelpers.cs` (`ReactionDelay`), `Simulation/Collisions.cs` (`KindName`), `Simulation/World.cs` (smoke message), `Simulation/Warnings.cs` (hostile-only rule)
- Create: `Drivers/CivilianDriver.cs`, `Drivers/PatrolDriver.cs`
- Test: `tests/NightPursuit.Core.Tests/FactionTests.cs`

**Interfaces:**
- Produces:
  - `enum Faction { Player, Hostile, Civilian, Police }`. Append only; nothing persists its values.
  - `AgentKind` gains `Civilian, PolicePatrol, PoliceCruiser, Interceptor, ArmoredSuv`, **after `Boss`**. Diagnostics' `% 6` casts rely on this order.
  - `static class KindRules { static Faction FactionOf(AgentKind k); static bool IsPolice(AgentKind k); static string DisplayName(AgentKind k) }`:
    - `FactionOf` gives Civilian → `Civilian`, the four police kinds → `Police`, everything else → `Hostile`;
    - `DisplayName` gives "CIVILIAN", "POLICE", "POLICE", "INTERCEPTOR", "POLICE SUV", and `Kind.ToString().ToUpperInvariant()` for the old kinds.
  - `AgentCar.Faction => KindRules.FactionOf(Kind)`. `BossCar` is unchanged and still Hostile.
  - `VehicleStats`, fields in order (Width, Length, Height, TopSpeed, Accel, SideSpeed, Armor, Points):
    - `Civilian = new(1.85, 4.5, 1.45, 38, 4, 2.5, 4, 0)`
    - `PolicePatrol = new(1.9, 4.8, 1.45, 72, 6.5, 4.0, 8, 0)`
    - `PoliceCruiser = new(1.9, 4.8, 1.45, 76, 7, 4.5, 10, 0)`
    - `Interceptor = new(1.9, 4.7, 1.25, 80, 9, 6, 10, 0)`
    - `ArmoredSuv = new(2.3, 5.2, 1.9, 70, 6, 3.5, 30, 0)`
  - `ReactionDelay`: Civilian 0.7, PolicePatrol 0.5, PoliceCruiser 0.4, Interceptor 0.3, ArmoredSuv 0.5.
  - `AgentCar.Create` picks these stats.
  - `DriverFor`:
    - Civilian → `CivilianDriver.Instance`;
    - every police kind → `PatrolDriver.Instance` (Tasks 8 and 9 swap drivers at run time).
  - `AgentCar` gains `double PlayerBlameUntil` (Task 3 uses it) and `double HitHeatCooldown` (Task 3).
  - `sealed class CivilianDriver : IDriver`, plain version (Task 5 adds the reactions):
    - keeps its lane, `State { double Lane; AvoidMemory Avoid; }`, with the lane set to the nearest lane centre on its first call;
    - target speed is `CruiseSpeed`;
    - lateral goes through `SteerAroundWrecks` with `ReactionDelay(Civilian)`;
    - **the player counts as a car ahead** (call 25), through a new `DriverHelpers.KeepClearOfPlayerAhead(AgentCar self, WorldView view, DriveIntent wish, CarAheadMemory memory, double reactionDelay, double dt) → DriveIntent`. It applies the same look-ahead and lane rules as `KeepClearOfCarAhead`, with the player as the only "car", and follows at `player.Speed − 2` when no neighbour lane is free. The plain drivers call it after `KeepClearOfCarAhead`. Enemy drivers do not use it, so their behaviour is unchanged;
    - it never fires or drops anything.
  - `sealed class PatrolDriver : IDriver`: the same lane driving, including the player rule, for police at Patrol level.
  - `Collisions.KindName` and `World`'s "{KIND} LOST YOU IN THE SMOKE" use `KindRules.DisplayName`.
  - `Warnings` hostile-behind rule: only `e.Faction == Faction.Hostile`. Civilian and police cars raise no warning in this task.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void New_kinds_have_their_faction() { /* Create each new kind → Faction Civilian / Police; Runner → Hostile; Boss stays Hostile */ }
[Fact] public void New_kinds_come_after_boss() { /* (int)AgentKind.Civilian == (int)AgentKind.Boss + 1 */ }
[Fact] public void Civilian_keeps_its_lane_at_cruise_speed() { /* straight track, civilian lane +5 at CruiseSpeed 30, 20 s alone → |Lateral − 5| < 0.05 every step after 1 s; Speed 30 ± 0.2 */ }
[Fact] public void Civilian_steers_round_a_wreck_in_its_lane() { /* wreck 150 m ahead in its lane → it passes the wreck with no WreckCollision within 15 s (it may return to its lane afterwards) */ }
[Fact] public void Civilian_never_runs_into_a_stopped_player() { /* player stopped in lane 0; civilian 200 m behind in lane 0 at 30 m/s; 20 s → no contact (no PlayerDamaged); also with both neighbour lanes blocked by StillDriver cars → it stops behind the player */ }
[Fact] public void Strike_spares_civilians_and_police() { /* spec §11: a civilian, a PolicePatrol and a Runner within strike range; BeginStrike then 1.6 s → Runner Wrecked (Satellite); civilian and police Driving */ }
[Fact] public void Missile_never_locks_or_hits_civilians_or_police() { /* only a civilian and a police car ahead in the cone → FindMissileTarget() null; a missile fired straight through them passes without destroying either */ }
[Fact] public void A_fast_civilian_behind_raises_no_warning() { /* civilian 20 m behind closing at 10 m/s → Warnings.Collect has no HostileBehind */ }
```
(Old runs staying identical is proven by every existing seeded test still passing; Task 4 adds `Zero_traffic_draws_nothing` once `TrafficRng` exists.)
- [ ] **Step 2: Run** `dotnet test tests/NightPursuit.Core.Tests --filter FullyQualifiedName~FactionTests`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement.** The missile and strike already filter on `Faction.Hostile`, so the faction from kind is the only change they need.
- [ ] **Step 4: Run the whole suite.** Expected: all pass. The existing `AgentPhysicsTests` assertion that `Create` gives Hostile still holds for the old kinds.
- [ ] **Step 5: Commit** `feat: civilian and police kinds with their own faction`.

### Task 2: The heat meter

**Files:**
- Create: `Police/HeatLevel.cs`, `Police/HeatRules.cs`, `Police/HeatMeter.cs`
- Modify: `Simulation/World.cs` (owns the meter, decay per step, hash), `Simulation/GameEvent.cs`
- Test: `HeatTests.cs`

**Interfaces:**
- Produces:
  - `enum HeatLevel { Calm, Watched, Patrol, Pursuit, Manhunt }`.
  - `static class HeatRules`, all the HT-1/HT-2 numbers in one place:
    - thresholds `Watched = 25, Patrol = 50, Pursuit = 70, Manhunt = 90`, and `Max = 100`;
    - `CivilianHit = 10, CivilianDestroyed = 25, PoliceHit = 15, SpeedTrap = 8, RoadblockHit = 15, PoliceDestroyed = 40, PoliceShot = 5`;
    - `DecayDelay = 5, Decay = 2, DecayNoPolice = 4, NoPoliceRadius = 300, BlameSeconds = 5, HitCooldown = 1`;
    - `LevelHysteresis = 5, MinPoliceLevelSeconds = 20` (call 9);
    - `static HeatLevel LevelOf(double heat)`;
    - `static string LevelName(HeatLevel)`: "CALM", "WATCHED", "PATROL", "PURSUIT", "MANHUNT".
  - `sealed class HeatMeter`:
    - `double Value`, `HeatLevel Level`, `double SinceOffence`, `double RiseFlash`, `double LevelTime` (seconds since the level was entered);
    - `HeatLevel? Set(double value)` sets the value and returns a level change if there is one (the caller publishes it);
    - `HeatLevel? Add(double amount)` clamps to 0–100, resets `SinceOffence`, sets `RiseFlash = 1` when it rises, and returns the new level if it changed;
    - `HeatLevel? Update(double dt, bool policeNear)` decays once `SinceOffence ≥ 5` (at −2/s, or −4/s when `!policeNear`), counts `RiseFlash` down, and returns a level change;
    - **Level rule (call 9):**
      - Rising uses the thresholds.
      - Falling from a level needs `Value` below its threshold − 5. For Patrol and above it also needs `LevelTime ≥ 20`.
      - A fall drops one level at a time per step.
    - Every timer counts whole steps internally (`int` step counters, converted to seconds), so 5 s is exactly 600 steps.
  - `World.Heat` (a `HeatMeter`).
  - `World.AddHeat(double amount, string reason)` publishes `HeatChanged(Time, amount, Value, reason)`, and on a level change publishes `HeatLevelChanged(Time, from, to)` and a banner:
    - **rising to** Watched: Message "HEAT: WATCHED - THE POLICE ARE TAKING NOTE" (Info);
    - **rising to** Patrol: Announce("POLICE PATROLS", "SPEED TRAPS AND ROADBLOCKS AHEAD", 2.5);
    - **rising to** Pursuit: Announce("POLICE PURSUIT", "DON'T LET THEM STOP YOU", 2.5);
    - **rising to** Manhunt: Announce("MANHUNT", "THEY WILL RAM YOU - SHAKE THEM WITH SMOKE", 2.5);
    - **falling:** Message "HEAT DOWN: {LEVEL}" (Good).
  - In `World.StepOnce`, after `Collisions.Resolve`: `Heat.Update(Step, policeNear)`.
    - `policeNear` is true when any Police-faction agent that is not Wrecked is within 300 m (`|dz|`) of the player.
    - It is also true when a police car is chasing (its `Driver` is not `PatrolDriver.Instance`) and `Blinded ≤ 0`, at any distance.
    - A level change found in `Update` publishes the same events and messages.
  - **Every level change also publishes a Message**, rising or falling, because a non-urgent banner is held back while an urgent one is showing:
    - rising: "HEAT: {LEVEL}" (Bad);
    - falling: "HEAT DOWN: {LEVEL}" (Good).
  - `GameEvent` records: `HeatChanged(double Time, double Amount, double Value, string Reason)` and `HeatLevelChanged(double Time, HeatLevel From, HeatLevel To)`.
  - `StateHash` adds `Heat.Value` and `Heat.SinceOffence`.

- [ ] **Step 1: Write failing tests**

```csharp
[Theory][InlineData(0, HeatLevel.Calm)][InlineData(24.9, HeatLevel.Calm)][InlineData(25, HeatLevel.Watched)][InlineData(50, HeatLevel.Patrol)][InlineData(70, HeatLevel.Pursuit)][InlineData(90, HeatLevel.Manhunt)][InlineData(100, HeatLevel.Manhunt)]
public void Levels_follow_the_table(double heat, HeatLevel level) { }
[Fact] public void Heat_stays_between_0_and_100() { /* Add(80) twice → 100; Set(3), 10 s of Update → 0 */ }
[Fact] public void Decay_waits_5_s_then_falls_2_per_s_with_police_near() { /* Add(60); 600 steps Update(policeNear: true) → 60 ± 1e-9; 600 more → 50 ± 1e-6 */ }
[Fact] public void Decay_is_4_per_s_with_no_police_near() { /* Add(60); 600 steps; 600 more with policeNear false → 40 ± 1e-6 */ }
[Fact] public void Rising_a_level_publishes_announces_and_messages() { /* World: AddHeat(55, "TEST") → HeatChanged, HeatLevelChanged(Calm→Patrol), Announcement title "POLICE PATROLS", Message "HEAT: PATROL"; RiseFlash 1 */ }
[Fact] public void A_rise_under_an_urgent_banner_still_shows_a_message() { /* Announce(..., urgent: true) first; AddHeat(55) → the Message "HEAT: PATROL" is published */ }
[Fact] public void Levels_hold_20_s_and_fall_5_below_the_threshold() { /* Add(75) → Pursuit; policeNear false; heat reaches 66 at 7.25 s and the level stays Pursuit; at 20 s (heat 60, below 65) → Patrol on that step; a heat of 66 after 20 s stays Pursuit */ }
[Fact] public void Falling_a_level_says_so() { /* Add(51), run until the level falls → HeatLevelChanged(Patrol→Watched) once heat < 45 and 20 s have passed, Message "HEAT DOWN: WATCHED" */ }
[Fact] public void Three_hits_of_10_from_25_reach_patrol() { /* Set(25); AddHeat(10) ×3 → Level Patrol (the scenario version with real civilians is Task 3) */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~HeatTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass. With no police, heat is 0, so nothing is published and the old hashes only gain constant terms.
- [ ] **Step 5: Commit** `feat: heat meter with levels, decay and announcements`.

### Task 3: Offences: blame and heat from your actions

**Files:**
- Create: `Simulation/World.Heat.cs`
- Modify: `Simulation/World.cs` (tracer hit loop, `Destroy`), `Simulation/Collisions.cs` (`CarVsCar` hook), `Simulation/HazardContacts.cs` (your own oil and caltrops), `Simulation/GameEvent.cs`
- Test: `OffenceTests.cs`

**Interfaces:**
- Consumes: `HeatRules`, `World.AddHeat` (Task 2); `PlayerBlameUntil` and `HitHeatCooldown` (Task 1).
- Produces:
  - `World.BlamePlayer(AgentCar a)` sets `a.PlayerBlameUntil = Time + 5`. It is called when:
    - your tracer hits the car;
    - `CarVsCar` registers a contact **that is your fault** (call 3): the `dz > 0` branch where you rear-end the car, or a side contact with `playerInto > agentInto`. A car rear-ending you or moving into your side is not your fault;
    - your own oil spins it (non-hostile `OilSlick` in `Collisions`);
    - your own caltrops puncture it.
  - `World.OffenceHit(AgentCar a, bool bullet)`, called on the same occasions as blame (not for oil or caltrops). The two kinds of occasion stay apart:
    - **your fault:** your tracer hit, your rear-end, your side contact;
    - **not your fault:** a contact the other car made.
    - **Civilian, when `HitHeatCooldown ≤ 0`:** `AddHeat(10, "CIVILIAN HIT")`; Message "CIVILIAN HIT  +10 HEAT" (Bad); no change to the score; `HitHeatCooldown = 1`.
    - **Police, bullet:** `AddHeat(5, "SHOT A POLICE CAR")`, every hit. Message "POLICE CAR SHOT  +5 HEAT" (Bad) at most once per second.
    - **Police, contact, when `HitHeatCooldown ≤ 0`:** `AddHeat(15, "HIT A POLICE CAR")`; Message "POLICE CAR HIT  +15 HEAT" (Bad); `HitHeatCooldown = 1`.
    - **Hostile:** nothing.
  - `HitHeatCooldown` counts down in `StepAgents`, next to `ContactCooldown`.
  - `World.Destroy` scoring by faction:
    - **Hostile:** as now (D54).
    - **Civilian:**
      - when `PlayerBlameUntil > Time`: `AddHeat(25, "CIVILIAN DESTROYED")`, Message "CIVILIAN DESTROYED  +25 HEAT" (Bad), `VehicleDestroyed(..., Points: 0, Faction.Civilian)`, no change to the score;
      - otherwise: Message "CIVILIAN CRASHED" (Info) and Points 0.
    - **Police:**
      - when blamed: `AddHeat(40, "DESTROYED A POLICE CAR")`, Message "POLICE CAR DESTROYED  +40 HEAT" (Bad), Points 0;
      - otherwise: Message "POLICE CAR CRASHED" (Info).
    - Wreck rules are unchanged (WRK-1/2): a civilian shot dead leaves a wreck that blocks the lane.
  - `VehicleDestroyed` gains a trailing `Faction Faction = Faction.Hostile`.
  - `StateHash` adds each agent's `PlayerBlameUntil` and `HitHeatCooldown`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Three_civilian_hits_reach_patrol_level() { /* spec §11 with a start of 25 (call 9): ram three different civilians (you rear-end them, via Collisions.Resolve) → Heat.Level Patrol; Score unchanged */ }
[Fact] public void Three_civilian_hits_from_zero_reach_watched() { /* the same from 0 → Level Watched, heat 30 (the spec example's gap, logged for the owner) */ }
[Fact] public void Holding_fire_on_a_civilian_adds_at_most_10_per_second() { /* civilian with a StillDriver (so it can't swerve) and armor 1000, 40 m ahead in the gun line; 3 s of Fire → Heat ≤ 30 + 1e-9 and ≥ 20; "CIVILIAN HIT" messages ≤ 3 */ }
[Fact] public void Shooting_a_civilian_dead_raises_heat_and_leaves_a_wreck() { /* civilian with a StillDriver and armor 4 in the gun line → VehicleDestroyed(Faction.Civilian, Points 0), a Wreck in its lane, heat ≥ 25, Score unchanged */ }
[Fact] public void A_car_hitting_you_costs_you_nothing() { /* a civilian rear-ends you (HIT FROM BEHIND), and a PoliceCruiser moves into your side (agentInto > playerInto) → no heat, PlayerBlameUntil unchanged (your armor still takes the normal contact damage) */ }
[Fact] public void Interceptor_ram_costs_you_no_heat() { /* heat 95 held; an Interceptor sideswipes you → Heat unchanged apart from decay, SinceOffence not reset */ }
[Fact] public void Police_bullets_cost_5_heat_each() { /* PolicePatrol armor 1000, 10 tracer hits → heat +50 ± 1e-9 */ }
[Fact] public void Ramming_a_police_car_costs_15_once_per_second() { /* two contacts 0.5 s apart → +15; another 1.1 s later → +30 */ }
[Fact] public void Enemy_caused_civilian_crash_costs_nothing() { /* Runner sideswipes a civilian into a spin and off the road (AgentVsAgent) → VehicleDestroyed(Faction.Civilian, Points 0), Message "CIVILIAN CRASHED", Score and Heat unchanged */ }
[Fact] public void Your_oil_crash_counts_against_you() { /* your OilSlick spins a civilian that then runs off the road within 5 s → +25 heat and Message "CIVILIAN DESTROYED  +25 HEAT"; Score unchanged */ }
[Fact] public void Blame_wears_off_after_5_s() { /* bump a civilian, wait 5.1 s, a Runner rams it off the road → no heat */ }
[Fact] public void Civilians_and_police_never_change_the_score() { /* blamed civilian destroyed, civilian hit, police shot, police destroyed → Score unchanged throughout; heat rises */ }
[Fact] public void Hostile_scoring_is_unchanged() { /* Runner shot dead → +100 as before; D54 AI-vs-AI kill still scores */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~OffenceTests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Keep every heat number in `HeatRules`.
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: offences raise heat and cost points only when you caused them`.

### Task 4: Traffic and the mission's traffic and police settings

**Files:**
- Create: `Missions/TrafficDefinition.cs`, `Missions/PoliceDefinition.cs`, `Traffic/TrafficDirector.cs`
- Modify: `Missions/MissionDefinition.cs`, `Missions/MissionLoader.cs`, `Missions/MissionDirector.cs`, `Missions/MissionRun.cs`, `Simulation/World.cs` (`TrafficRng`)
- Test: `TrafficTests.cs`, `MissionTests.cs` (loader rows)

**Interfaces:**
- Produces:
  - `sealed record TrafficDefinition(double CarsPerKm)`.
  - `sealed record PoliceDefinition(double Presence, double StartingHeat)`.
  - `MissionDefinition` gains trailing `TrafficDefinition? Traffic = null, PoliceDefinition? Police = null`.
  - Loader, in the strict style of `boss`:
    - `"traffic": { "carsPerKm": 0–12 }`. An unknown key fails with "traffic.X is not a known field (carsPerKm)". An out-of-range value fails with "traffic.carsPerKm must be from 0 to 12 (got N)".
    - `"police": { "presence": 0–2, "startingHeat": 0–100 }`. Both are optional inside the section, with defaults 1 and 0. Errors are worded the same way. **Defaults:** with no `police` section at all, presence is 0 (no police). With a section but no `presence` key, presence is 1.
    - A wave `kind` or a boss phase `behaviour` must be one of the six enemy types (call 26). For example "Civilian" fails with "waves[0].kind 'Civilian' is not an enemy type (Runner, Blocker, Pursuer, Gunner, Dropper, Armored)".
  - `World.TrafficRng`: an `IRng`, default `new SeededRng(7919)`. `MissionRun.Rebuild` sets it to `new SeededRng(seed * 31 + 7)`. Only `TrafficDirector` and `PoliceDispatcher` draw from it.
  - `sealed class TrafficDirector(TrafficDefinition def)`, with `void Update(World w)`, called from `MissionDirector.Update`. It does nothing when `CarsPerKm == 0`.
    - **Target:** `round(CarsPerKm × 2.0, AwayFromZero)` civilians alive within −500…+1500 m of the player. The count covers the whole window up to the cull line, so cars just spawned at 1400–1450 m are counted.
    - **Start of a mission:** on its first `Update` the director seeds the whole target at once. The cars are spread evenly across −400…+1450 m, each with a random offset of ±40 m, keeping 60 m clear of the player, in random lanes. Nothing has been drawn yet, so this is not pop-in, and the traffic doesn't arrive as one platoon.
    - **Spawning:** while below the target, at most one spawn per 0.25 s of game time. Each spawn goes 1250–1450 m ahead, in a random lane, at 25–35 m/s. If the player is slower than 25 m/s, it goes 400–450 m behind at 30–35 m/s instead.
    - **Rejected spots:** a spot within 40 m of another car in the same lane, within 60 m of a wreck or roadblock, or within 30 m of any hazard in that lane. The director then tries the next lane, and otherwise waits.
    - The spawned car is `AgentCar.Create(w.NextId(), Civilian, …)`, with `CruiseSpeed` set to its speed.
    - The existing `IsGone` window culls civilians.
  - `MissionRun.Rebuild` applies `Police?.StartingHeat` through `World.Heat.Set`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Traffic_spawns_out_of_sight_and_spaced() { /* carsPerKm 6, player 50 m/s for 60 s: every new civilian at its first step has dz ≥ 1250 (or ≤ −400 while the player was slower than 25 m/s); never within 40 m of another car in its lane */ }
[Fact] public void Traffic_density_stays_near_the_target() { /* carsPerKm 4 (target 8) → at every 5 s sample from 0 to 60 s, civilians within −500..+1500 between 6 and 8 */ }
[Fact] public void Traffic_starts_spread_out() { /* carsPerKm 6 → after the first Update, 12 civilians; no two in one lane within 40 m; at least 3 of 4 window quarters hold a car */ }
[Theory] /* MissionTests rows: waves[0].kind "Civilian" and "PolicePatrol", boss.phases[0].behaviour "Interceptor" → each rejected with the "is not an enemy type" message */
public void Only_enemy_types_can_be_waves_or_boss_behaviours(string json, string expected) { }
[Fact] public void Slow_player_gets_traffic_from_behind() { /* player held at 10 m/s → some civilian spawns at −400..−450 */ }
[Fact] public void Traffic_never_spawns_on_a_wreck() { /* wreck placed 1300 m ahead in lane 0 → no civilian spawns within 60 m of it */ }
[Fact] public void Zero_traffic_draws_nothing() { /* carsPerKm 0 → TrafficRng untouched, no civilians */ }
[Theory] /* MissionTests rows: traffic {"carsPerKm": 13} → "traffic.carsPerKm must be from 0 to 12 (got 13)"; traffic {"density": 3} → "traffic.density is not a known field (carsPerKm)"; police {"presence": 3} → "police.presence must be from 0 to 2 (got 3)"; police {"startingHeat": -1}; police {"heat": 5} → not a known field (presence, startingHeat) */
public void Bad_traffic_and_police_name_the_field(string section, string expected) { }
[Fact] public void Starting_heat_is_applied() { /* police { presence 1, startingHeat 55 } → new MissionRun → World.Heat.Value 55, Level Patrol */ }
```
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~TrafficTests|FullyQualifiedName~MissionTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass. mission-01 is not changed until Task 15.
- [ ] **Step 5: Commit** `feat: civilian traffic and the mission traffic and police settings`.

### Task 5: Civilians react to danger

**Files:**
- Modify: `Drivers/CivilianDriver.cs`, `Drivers/WorldView.cs`, `Drivers/DriverHelpers.cs`, `Simulation/World.cs` (`UpdateBlindness` skips civilians; `PlayerFiring` flag)
- Test: `CivilianTests.cs`

**Interfaces:**
- Consumes: `World.Heat` (Task 2).
- Produces:
  - `WorldView` gains:
    - `HeatLevel HeatLevel`;
    - `bool PlayerFiring` (the guns fired a tracer this step, from a `World.PlayerFiredThisStep` flag cleared at the start of each step);
    - `double Time`.
  - `DriverHelpers.FreeNeighbourLane(AgentCar self, WorldView view, double behind, double ahead, int preferSide) → double?`: the neighbouring lane centre with no car (player included) and no wreck or roadblock within −behind…+ahead of `self`.
    - When both neighbours are free, it takes the one on `preferSide` (−1 or +1).
    - It returns null if both neighbours are taken.
  - `CivilianDriver` reactions. `State` gains `Mode { Cruise, Swerve, PullOver, Shaken }` and `ModeTimer`. In priority order:
    1. **Shaken.** `HitFlash > 0` starts it, for 3 s: speed 0.5 × cruise, target the shoulder on its nearer side (±8.0).
    2. **Pull over.** The heat level is Pursuit or above, and the player, or a Police-faction car whose `Driver` is not `PatrolDriver.Instance` (that is, one that is chasing), is within 150 m. Target the shoulder on its nearer side (**±8.0**, clear of the outer lane: a 1.85 m car at 8.0 spans 7.075–8.925) at 12 m/s, then return to its lane after 3 s with no trigger. Off-road destruction still needs |Lateral| > 8.5 plus a spin, push or puncture, so the shoulder is safe.
    3. **Swerve.** Either trigger starts it:
       - a non-civilian car (player, hostile or police) is behind in its lane, closing faster than 3 m/s, with **time to contact ≤ 2.5 s** (gap ÷ closing speed);
       - `PlayerFiring` while it is 0–80 m ahead of the player with |ΔL| < 2.5.

       The response starts on the same step, with no reaction delay:
       - it moves to `FreeNeighbourLane(…, behind: max(15, closing × 2.5), ahead: 40, preferSide: away from the threat's lateral, or toward the road edge when level)`;
       - it runs at 0.7 × cruise for 2 s;
       - with no free lane, it slows to 0.6 × cruise and holds its lane.
    4. **Smoke.** Inside any `SmokeCloud` (`Covers`): speed 0.6 × cruise, and it keeps its lane.
    5. **Cruise:** the plain driving from Task 1.

    Keep-clear (`KeepClearOfCarAhead`) still applies after the decision.
  - `World.UpdateBlindness` never blinds a Civilian-faction car and never shows the "LOST YOU IN THE SMOKE" message for one.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Civilian_moves_aside_for_a_fast_car_behind() { /* civilian lane 0 at 30; player 60 m behind in lane 0 held at 50 (TTC 3 s) → within 3 s civilian |Lateral| ≥ 3.1 (clear of the player's lane) and no contact */ }
[Fact] public void Civilian_swerves_away_from_the_threat() { /* threat behind at lateral −1 → it moves to lane +5 */ }
[Fact] public void Civilian_swerves_out_of_the_gun_line() { /* civilian 60 m ahead lane 0, player firing straight → civilian |Lateral| ≥ 1.8 within 1.5 s and Speed < 0.75·cruise */ }
[Fact] public void Civilians_pull_over_in_a_pursuit() { /* heat 75, a PoliceCruiser with a test driver (not PatrolDriver) 100 m behind the civilian, player 300 m away → civilian reaches |Lateral| ≥ 7.5 within 5 s and Speed ≤ 12.5; never destroyed */ }
[Fact] public void A_pulled_over_civilian_clears_the_outer_lane() { /* civilian pulled over on the +side, player passing in lane +5 at 50 m/s → no contact */ }
[Fact] public void Civilian_is_not_blinded_by_smoke_but_slows() { /* SmokeCloud covering it → Blinded stays 0, no "LOST YOU" message, Speed ≤ 0.6·cruise + 0.3, |Lateral − lane| < 0.3 */ }
[Fact] public void A_hit_civilian_slows_and_edges_over() { /* HitFlash set → 3 s Shaken: speed ≤ 0.5·cruise + 0.5 by 2 s, Lateral moving toward its nearer edge */ }
[Fact] public void Civilian_holds_lane_when_both_neighbours_are_taken() { /* cars in both neighbour lanes alongside; danger behind → stays in lane, slows */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~CivilianTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: civilians swerve, pull over, slow in smoke and when hit`.

### Task 6: Speed traps and roadblocks

**Files:**
- Create: `Hazards/ILaneBlocker.cs`, `Hazards/SpeedTrap.cs`, `Hazards/Roadblock.cs`
- Modify: `Hazards/Wreck.cs` (implements `ILaneBlocker`), `Drivers/DriverHelpers.cs` (`IsBlocked` uses `ILaneBlocker`), `Simulation/Collisions.cs` (player and agents against roadblocks), `Simulation/World.cs` (speed-trap check, roadblock clearing), `Simulation/GameEvent.cs`
- Test: `RoadsideTests.cs`

**Interfaces:**
- Consumes: `World.AddHeat` (Task 2).
- Produces:
  - `interface ILaneBlocker { double Distance { get; } bool Blocks(double lateral, double halfWidth); }`.
    - `Wreck` implements it by calling its **existing overlap expression unchanged** (the same arithmetic in the same order, so floating-point results, and with them every seeded test, stay identical).
    - `DriverHelpers.IsBlocked` and `AvoidWrecks` / `SteerAroundWrecks` treat every `ILaneBlocker` as a wreck, so every AI car swerves for roadblocks.
    - **Look-ahead for a `Roadblock`:** `max(120, self.Speed × 5)`, so a car in the far lane has time to cross to the gap. A civilian needs about 4 s and 120 m; a Runner at 50 m/s needs about 142 m. Wrecks keep the existing 120 m, so old results don't change.
  - `sealed class SpeedTrap : Hazard`:
    - `double LimitMph` (60, 70 or 80); `Lateral = ±9.0`, at the road edge on a random side; `bool Triggered`;
    - it never collides with anything;
    - it expires 600 m behind, like the other hazards.
  - Speed-trap check in `World.StepOnce`, after the player moves: if `PrevDistance < trap.Distance ≤ Distance`, `!Triggered`, and the player is above the limit, then:
    - `AddHeat(8, "SPEED TRAP")`;
    - Message `$"SPEED TRAP {mph:0} IN A {limit:0}  +8 HEAT"` (Bad);
    - `SpeedTrapPassed(Time, mph, limit)`;
    - set `Triggered`.

    At or under the limit: `Triggered`, with Message "SPEED TRAP: UNDER THE LIMIT" (Good).
  - `sealed class Roadblock : Hazard, ILaneBlocker`:
    - fields: `int GapLane` (0–2, an index into `Track.LaneCentres`), `bool HitPlayer`, and `const double Depth = 2.0, BarrierHalfWidth = 2.3`;
    - the two blocked lanes are the other two indexes;
    - `Blocks(lat, hw)` is true when `|lat − blockedCentre| < BarrierHalfWidth + hw` for either blocked lane;
    - `Lateral` = the gap lane's centre (for radar and warnings);
    - `static Roadblock Place(double distance, int gapLane)`.
  - Player against a roadblock (`Collisions`), when the player overlaps a blocked span with `|dz| < Depth/2 + player half length`:
    - first time (`!HitPlayer`): `DamagePlayer(25, "HIT A ROADBLOCK")`, `AddHeat(15, "ROADBLOCK")`, `RoadblockHit(Time)`, speed capped to 5;
    - after that it blocks like a wreck, pushing the player back to the barrier's front. Steering crabs you sideways along it at **5 m/s** (the D5 crab is 3 m/s for wrecks), so the farthest you can be from the gap, 8.25 m, takes 1.65 s.
  - Agents against a roadblock: treated as a wreck (`Destroy(…, WreckCollision)`). A boss is never destroyed by one, because roadblocks are removed when the boss stage starts (Task 7).
  - **Gap guarantee:** each step, if any `Wreck` lies from 150 m before a roadblock to 30 m after it, inside its gap lane (`Wreck.Blocks(gapCentre, 1.0)`), the roadblock is removed, with Message "ROADBLOCK CLEARED" (Good).
  - `GameEvent`s: `SpeedTrapPassed(double Time, double Mph, double LimitMph)` and `RoadblockHit(double Time)`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Passing_a_trap_above_the_limit_costs_8_heat_once() { /* trap limit 70 at +50 m, Player.Speed reset to 40 m/s (89 mph) every step → +8, message "SPEED TRAP 89 IN A 70  +8 HEAT", SpeedTrapPassed; a second pass of the same trap impossible (Triggered) */ }
[Fact] public void Passing_under_the_limit_is_free() { /* player 30 m/s (67 mph), limit 70 → no heat, "SPEED TRAP: UNDER THE LIMIT" */ }
[Fact] public void Roadblock_always_leaves_its_gap_lane_open() { /* for gapLane 0..2: Blocks(gap centre, 0.95) false; Blocks(each other lane centre, 0.95) true */ }
[Fact] public void Driving_through_the_gap_is_free() { /* player in the gap lane at 50 m/s through the roadblock → no damage, no heat */ }
[Fact] public void Hitting_a_roadblock_costs_25_armor_15_heat_once() { /* player in a blocked lane → Armor −25, Speed ≤ 5, heat +15, RoadblockHit once; pushing on → no further damage */ }
[Fact] public void AI_cars_swerve_into_the_gap() { /* gap in lane +5; a civilian (30 m/s) and a Runner (50 m/s) in lane −5, 400 m before it → both pass through without WreckCollision */ }
[Fact] public void A_roadblock_whose_gap_is_blocked_is_cleared() { /* Review Focus 1: wreck placed 120 m before the roadblock in its gap lane → next step Roadblock gone, "ROADBLOCK CLEARED" */ }
[Fact] public void You_crab_along_a_roadblock_at_5_ms() { /* stopped against a blocked lane, steering held toward the gap → reach the gap lane within 1.7 s */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~RoadsideTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass. The wreck tests still pass through `ILaneBlocker`.
- [ ] **Step 5: Commit** `feat: speed traps and roadblocks with a guaranteed gap`.

### Task 7: The police dispatcher at Patrol level

**Files:**
- Create: `Police/PoliceDispatcher.cs`
- Modify: `Drivers/PatrolDriver.cs`, `Missions/MissionDirector.cs` (creates the dispatcher when `Police?.Presence > 0`, and calls it after traffic)
- Test: `PoliceTests.cs`

**Interfaces:**
- Consumes: `HeatMeter` (Task 2), `SpeedTrap`, `Roadblock` (Task 6), `TrafficRng` (Task 4).
- Produces:
  - `sealed class PoliceDispatcher(PoliceDefinition def)`, with `void Update(World w, MissionStage stage)`. All spawn draws come from `w.TrafficRng`.
  - **At Patrol or above:**
    - keep `round(2 × presence, AwayFromZero)` police cars of any kind within −500…+1500 m;
    - missing ones spawn as `PolicePatrol` 1250–1450 m ahead in a free lane, at 28–32 m/s, using the same spawn-spot rules as traffic;
    - **at the start of a mission** whose starting heat is already at Patrol or above, the patrol cars are seeded across the window like traffic (Task 4).
  - **Speed traps:** the first `SpeedTrap` is placed 700 m ahead **on the step the level first reaches Patrol**. After that `NextTrapAt` steps by 1500 m, and each time the player passes it a new trap is placed 700 m ahead.
  - **Roadblocks:** the first `Roadblock` is placed 800 m ahead **on the step the level first reaches Patrol** (outside the boss stage). After that `NextRoadblockAt` steps by 3000 m, with one live roadblock at a time.
    - The gap lane is random. If that lane has a wreck within 150 m of the spot, the next lane is used.
    - If every lane does, the roadblock is skipped and `NextRoadblockAt` moves on 500 m.
  - **Boss stage:** on the step `MissionDirector` enters the boss stage, every `Roadblock` is removed. None are placed during it.
  - **Police cap (call 14):** at most 6 police within the window, filled in priority order: SUVs, interceptors, cruisers, patrols. Surplus patrol cars are removed only when out of sight (more than 1200 m ahead or more than 300 m behind).
  - **Below Patrol:** no new police, traps or roadblocks. The existing ones stay until they are culled.
  - **`PatrolDriver`:**
    - drives like a civilian in its lane at its cruise speed, keeping clear and avoiding wrecks and roadblocks;
    - it never pulls over and is never shaken;
    - it is blinded by smoke like an enemy (the default `UpdateBlindness`).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void No_police_below_patrol_level() { /* presence 1, heat 40, 60 s at 50 m/s → no Police-faction car, no SpeedTrap, no Roadblock */ }
[Fact] public void Patrol_level_keeps_patrol_cars_in_traffic() { /* presence 1, heat 55 (police near keeps decay at 2/s; re-Set heat 55 each 10 s) → after 30 s, 2 PolicePatrol within the window, none spawned (after the first update) with dz < 1250 */ }
[Fact] public void Patrol_level_places_traps_every_1500_m() { /* heat raised to 55 at distance D → a SpeedTrap at D + 700 on that step, then one every 1500 m; limits in {60,70,80} */ }
[Fact] public void Patrol_level_places_one_roadblock_at_a_time() { /* the Diagnostics-style Autopilot drives (so the player takes the gaps), heat held at 55 for 7000 m → first roadblock at reach + 800, then 3000 m apart; never two alive at once */ }
[Fact] public void Boss_stage_clears_roadblocks() { /* a roadblock alive when the approach ends → removed on the step the boss appears; none placed afterwards */ }
[Fact] public void Starting_at_patrol_seeds_patrol_cars() { /* startingHeat 55, presence 1 → after the first update, 2 PolicePatrol spread across the window */ }
[Fact] public void Presence_zero_means_no_police_ever() { /* heat 100, presence 0 → no police car, trap or roadblock */ }
[Fact] public void Patrol_cars_ignore_you() { /* PolicePatrol beside the player at Patrol level for 10 s → never targets the player's lane while the player is alongside; no contact */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~PoliceTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: police patrols, speed traps and roadblocks at patrol level`.

### Task 8: Pursuit: chasing and boxing in, never all three lanes

**Files:**
- Create: `Police/PoliceLanes.cs`, `Drivers/ChaseDriver.cs`
- Modify: `Police/PoliceDispatcher.cs`, `Drivers/PatrolDriver.cs` (its lane goes through `MayEnter` too)
- Test: `PoliceTests.cs`

**Interfaces:**
- Consumes: `PatrolDriver`, `PoliceDispatcher` (Task 7), `DriverHelpers.AttackSpeed`, `HoldGap`, `PassingLane`.
- Produces:
  - `static class PoliceLanes` (call 17):
    - `static bool Covers(AgentCar car, double laneCentre)`: `|car.Lateral − laneCentre| < 2.5 + car.Stats.Width / 2`.
    - `static int CoveredLanes(World w, AgentCar? except)`: the number of lane centres covered by moving (Driving) police cars within 60 m (`|dz|`) of the player, `except` excluded.
    - `static double SpeedCap(World w, AgentCar self, double wanted)` is applied **every step to every police car's intent**, after its driver decides. If `self` sits within 60 m of the player and the lanes covered by the other police plus `self` would be all three, `self`'s target speed is capped so it falls back to more than 45 m from the player, at the player's speed − 4 while it is within 60 m. Otherwise `wanted` is returned unchanged.
    - `static bool MayEnter(World w, AgentCar self, double laneCentre)`: the same count with `self` moved to `laneCentre`. Every police driver checks its target lane with it. If the lane is refused, the driver keeps its current lane, or the nearest lane that is allowed.
  - `enum ChaseRole { Trail, Front, Side }`, held in `AgentCar.DriverState` as `ChaseDriver.State { ChaseRole Role; int Side; AvoidMemory Avoid; }`.
  - `sealed class ChaseDriver : IDriver`:
    - **Trail:** in the player's own lane, 20 m behind, at `HoldGap(player, gap, −20, 1.2, −6, 20)`. When the player is below 5 m/s it stops 6 m behind (bumper to bumper).
    - **Front:** passes on the side with room (`PassingLane`), then takes the player's lane about 12 m ahead, at speed `max(8, player.Speed − 4)` while within 25 m ahead.
    - **Side:** holds |ΔD| < 3 in the neighbour lane on its `Side`, at the player's speed but **never below 3 m/s**, so it rolls on past a stopped player. It never steers into the player (target lateral ≥ 2.7 m from the player's lateral).
    - **Near a roadblock:** within 150 m before a roadblock, Front and Side act as Trail, so no police car stops in the gap.
    - It never rams at Pursuit level.
    - Blinded police use `BlindDriver` (unchanged), so smoke breaks a box-in.
  - **Dispatcher at Pursuit:**
    - every `PolicePatrol` within 600 m switches to `ChaseDriver` (Role Trail);
    - cruisers spawn until `min(3, round(2 × presence, AwayFromZero))` chasers exist, 350–450 m behind at the player's speed + 15, in a lane that `MayEnter` allows;
    - each step it gives Front to the nearest chaser, Side to the next, and Trail to the rest;
    - when the level drops below Pursuit, chasers get `PatrolDriver` with `CruiseSpeed = 25`, and Message "POLICE BACKING OFF" (Good) is shown once.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Pursuit_sends_cruisers_from_behind() { /* presence 1, heat 75 held, player 45 m/s → within 35 s two Police cars on ChaseDriver within 60 m */ }
[Fact] public void Front_car_slows_you_but_never_below_8() { /* Front role 12 m ahead in your lane, player gas held → its Speed ≥ 8 − 1e-6 at every step, and ≤ player − 3.5 at every step before the first contact */ }
[Theory][InlineData(-5)][InlineData(0)][InlineData(5)]
public void Police_never_fill_all_three_lanes_around_you(double playerLane) { /* Review Focus 1: presence 2, heat 85, 3 chasers + 2 patrol cars placed in all three lanes near you; 30 s; every step: PoliceLanes.CoveredLanes(w, null) ≤ 2 (covers counts a car between lanes in both) */ }
[Fact] public void Braking_breaks_a_box_in() { /* Front + Side in place at 40 m/s; brake 1.5 s, then steer toward the lane Side isn't in → within 4 s the player is in a lane with no police within 30 m ahead, and never Counting for busted */ }
[Fact] public void Trail_stops_behind_a_stopped_player_and_side_rolls_on() { /* player stopped; Trail ends 6 m ± 0.5 behind in your lane; Side keeps ≥ 3 m/s and leaves |ΔD| < 3 within 2 s */ }
[Fact] public void No_police_car_stops_in_a_roadblock_gap() { /* roadblock 140 m ahead, chasers in Front and Side → both behind the player until it is passed */ }
[Fact] public void Smoke_shakes_the_police() { /* drop smoke with chasers 20 m behind → they become Blinded, slow to ≤ 0.6·cruise + 1; no bust possible while Blinded (Task 10 checks) */ }
[Fact] public void Police_back_off_when_heat_drops() { /* heat 75 → 69: chasers switch to PatrolDriver at 25 m/s; "POLICE BACKING OFF" once */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~PoliceTests`. Expected: FAIL (new tests).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: pursuit level chases and boxes you in, never all three lanes`.

### Task 9: Manhunt: interceptors and rolling roadblocks

**Files:**
- Create: `Drivers/InterceptorDriver.cs`, `Drivers/RollingRoadblockDriver.cs`
- Modify: `Police/PoliceDispatcher.cs`
- Test: `PoliceTests.cs`

**Interfaces:**
- Consumes: `PoliceLanes` (Task 8), `DriverHelpers.AttackSpeed`, `AttackLateral`, `AttackSeconds`, `RamSeconds`.
- Produces:
  - `sealed class InterceptorDriver : IDriver`: the Plan 1 pursuer pattern (come alongside, ram, retreat), using the shared attack helpers, with all lanes put through `MayEnter`. Hits on the player use the existing `CarVsCar` damage. Police ramming you gives no heat.
  - `sealed class RollingRoadblockDriver : IDriver`. Two SUVs share one `RollingRoadblock` record held by the dispatcher: `Slot` 0 and 1, `int OpenLane`, `bool Formed`.
    - **Overtaking:** each SUV passes the player on the side with room (`PassingLane`) at the player's speed + 15.
    - **Forming:** once both are at least 60 m ahead, the open lane is **locked**: the lane farthest from the player's lane at that moment, or a random outer lane (from `TrafficRng`) if the player is in the middle. The two SUVs take the two other lanes, side by side, and `Formed` is set. The open lane never changes after that.
    - **Speed:** formed SUVs hold 40 m ahead at `HoldGap(player, gap, 40, 1.0, −8, 10)`. Once the player is within 40 m, they ride at the player's speed − 6, so the player closes gently and must take the gap or brake.
    - Lanes go through `MayEnter`, and the `SpeedCap` rule applies to them.
    - If one SUV is wrecked or blinded, the other switches to `ChaseDriver` (Trail), and the record is dropped.
  - `World.RollingRoadblocks` (`IReadOnlyList<RollingRoadblock>`, kept by the dispatcher) lets warnings, radar and drawing find the open lane.
  - **Dispatcher at Manhunt:**
    - `min(2, round(presence, AwayFromZero))` interceptors (`Interceptor` kind), spawned behind like cruisers;
    - when `presence ≥ 1`, one pair of `ArmoredSuv`s on `RollingRoadblockDriver`, spawned 350–450 m behind at the player's speed + 15 (SUV top speed 82);
    - while a formed pair is within 80 m of the player, the Side chaser acts as Trail;
    - the Pursuit cruisers remain;
    - total police within the window is capped at 6 in the call-14 priority order.
  - Below Manhunt: interceptors and SUVs fall back as in Task 8.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Manhunt_brings_interceptors_that_ram() { /* presence 1, heat 95 held, 60 s at 50 m/s → an Interceptor exists and at least one PlayerDamaged "SIDESWIPED" from it */ }
[Fact] public void Rolling_roadblock_always_leaves_a_lane_open() { /* presence 1, heat 95, up to 120 s; once Formed: the SUVs' lanes are adjacent, OpenLane never changes, and no police car covers OpenLane within 60 m of the player */ }
[Fact] public void Rolling_roadblock_slows_to_your_speed_minus_6() { /* formed pair, player within 40 m → their Speed ≤ player − 6 + 0.5 */ }
[Fact] public void Rolling_roadblock_gap_is_locked_when_it_forms() { /* the player changes lane after the pair forms → OpenLane unchanged */ }
[Fact] public void Manhunt_never_exceeds_6_police_nearby() { /* presence 2, heat 100, 90 s → police within the window ≤ 6 every step */ }
[Fact] public void Police_never_fill_all_three_lanes_in_a_manhunt() { /* as Task 8's theory at heat 95 with SUVs and interceptors */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~PoliceTests`. Expected: FAIL (new tests).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: manhunt interceptors and rolling roadblocks`.

### Task 10: Busted

**Files:**
- Create: `Police/BustedWatch.cs`
- Modify: `Simulation/World.cs` (watch per step, `BustPlayer`), `Missions/MissionRun.cs` (the respawn heat rule), `Simulation/GameEvent.cs`
- Test: `BustedTests.cs`

**Interfaces:**
- Consumes: `HeatMeter` (Task 2).
- Produces:
  - `sealed class BustedWatch`:
    - `const double Seconds = 3, SpeedLimit = 2.235, Radius = 15`;
    - `double Held` (seconds held so far, 0 when not counting); `bool Counting => Held > 0`; `int SecondsLeft => (int)Math.Ceiling(Seconds − Held)`;
    - `bool Update(World w, double dt)` returns true on the step the bust happens.
  - **Counting** happens when all of these hold: the player is Driving; the heat level is Pursuit or above; `Player.Speed < SpeedLimit`; and there is a Police-faction agent that is Driving and not Blinded with `sqrt(dz² + dl²) ≤ 15`.

    Otherwise `Held` resets to 0. If it had been counting, it publishes Message "BROKE FREE" (Good) once.
  - `World.Busted` (a `BustedWatch`) runs in `StepOnce` after the heat update.
  - `World.BustPlayer()`:
    - player `State = Wrecked`, `StateTimer = 3` (the same flow as `WreckPlayer`);
    - publishes `PlayerBusted(Time)` and `PlayerWrecked(Time, "BUSTED")`, Message "BUSTED - YOU LOSE A SPARE CAR" (Bad), and `Announce("BUSTED", "YOU LOSE A SPARE CAR", 3, urgent: true)`;
    - sets `World.BustedPending = true`.
  - `MissionRun.Respawn`: if `World.BustedPending`, set the heat to 50 and clear the flag.
    - Use a `World.SetHeat(50, "BUSTED")` that publishes `HeatChanged` and, when the level changes, `HeatLevelChanged`.
    - It also shows Message "HEAT SET TO 50: PATROL" (Info).
    - A wreck leaves heat as it was. `Busted.Held` resets to 0.
  - `Held` counts whole steps internally, so 3 s is exactly 360 steps.
  - `StateHash` adds `Busted.Held`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Busted_needs_pursuit_level() { /* Review Focus 2: heat 60, player stopped, PolicePatrol 8 m away for 5 s → never Counting, no PlayerBusted */ }
[Fact] public void Busted_after_3_s_held() { /* heat 75, stopped, cruiser 8 m behind (StillDriver) → PlayerBusted on step 360 exactly; SpareCars−1 after the respawn; heat 50 after respawn with HeatChanged and "HEAT SET TO 50: PATROL" published */ }
[Fact] public void Countdown_shows_from_the_first_step_and_cancels_above_5_mph() { /* Counting true on the first qualifying step with SecondsLeft 3; at 1.5 s raise speed to 3 m/s → Held 0, "BROKE FREE" once, no bust */ }
[Fact] public void Blinded_police_cannot_bust_you() { /* cruiser Blinded > 0 within 8 m → never Counting */ }
[Fact] public void Nothing_counts_while_wrecked_or_respawning() { /* player Wrecked or MissionRun Respawning/Paused → Held stays 0 */ }
[Fact] public void A_wreck_respawn_keeps_heat() { /* heat 80, player wrecked by damage → after respawn heat ≈ 80 minus decay, not 50 */ }
```
- [ ] **Step 2: Run** `--filter FullyQualifiedName~BustedTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass.
- [ ] **Step 5: Commit** `feat: busted countdown, breaking free and the respawn heat rule`.

### Task 11: Warnings, messages and the state hash

**Files:**
- Modify: `Simulation/Warnings.cs`, `Simulation/World.cs` (`StateHash`), `tests/.../WarningTests.cs`, `tests/.../WorldLoopTests.cs`
- Test: `WarningTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `WarningKind`, in priority order with the new kinds inserted:
    `MissileIncoming, PursuerAlongside, PoliceAlongside, GunnerAiming, HazardAhead, WreckAhead, RoadblockAhead, BlockerAhead, BossBehind, HostileBehind, PursuerBehind, PoliceBehind, SpeedTrapAhead`. That is 13 kinds, under the limit of 32. Inserting keeps the existing kinds in the same relative order.
  - Rules:
    - **PoliceAlongside:** level at Pursuit or above, a Police-faction car that is Driving with −3 < dz < 4 and |dL| < 6 gives "POLICE LEFT/RIGHT", with an arrow on its side. The sides of several police cars are combined as the pursuer rule does.
    - **PoliceBehind:** level at Pursuit or above, the nearest police car 3–120 m behind gives `$"POLICE BEHIND {d:0} m"`, with an arrow on its side.
    - **RoadblockAhead:** the nearest `Roadblock` 0–400 m ahead gives `$"ROADBLOCK {d:0} m - GAP {LEFT|CENTRE|RIGHT}"`. A formed rolling roadblock (Task 9) 0–400 m ahead gives `$"ROLLING ROADBLOCK {d:0} m - GAP {…}"` from its locked `OpenLane`; the nearer of the two wins. The arrow points to the gap lane; there is no arrow when you are already within 1.5 m of the gap's centre.
    - **SpeedTrapAhead:** the nearest untriggered `SpeedTrap` 0–400 m ahead gives `$"SPEED TRAP {limit:0} MPH  {d:0} m"`, with no arrow.
    - Civilians raise no warning of any kind. Police at Patrol level raise none either; their light bars and the radar show them.
  - The App's `Overlays.WarningBrush` colours are set in Task 14. Core only adds the kinds.
  - `StateHash` (completing Tasks 2, 3 and 10): adds `Heat.Value`, `Heat.SinceOffence`, `Busted.Held`, and for each agent `PlayerBlameUntil` and `HitHeatCooldown`, plus for each `SpeedTrap` its `Triggered` and for each `Roadblock` its `GapLane` and `HitPlayer`. Each one is covered by a per-item case added to `WorldLoopTests.Hash_covers_the_state_that_shapes_what_happens_next`.
  - `WarningTests.No_event_goes_unshown` is extended: `HeatChanged`, `HeatLevelChanged`, `SpeedTrapPassed`, `RoadblockHit` and `PlayerBusted` must each come with a Message or an Announcement in the same step.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Roadblock_warning_names_the_gap_and_points_to_it() { /* roadblock gap RIGHT 300 m ahead, player in lane 0 → "ROADBLOCK 300 m - GAP RIGHT", Side +1, ShowArrow */ }
[Fact] public void Rolling_roadblock_warns_with_its_gap() { /* formed SUV pair 50 m ahead, OpenLane LEFT → "ROLLING ROADBLOCK 50 m - GAP LEFT", arrow left */ }
[Fact] public void Speed_trap_warns_from_400_m() { /* trap 70 at 390 m → "SPEED TRAP 70 MPH  390 m"; at 410 m → none */ }
[Fact] public void Police_warn_only_at_pursuit() { /* cruiser alongside: heat 60 → no warning; heat 75 → "POLICE LEFT" or "POLICE RIGHT" with arrow */ }
[Fact] public void Civilians_never_warn() { /* civilians alongside, behind closing fast, ahead in lane → no warning of any kind */ }
[Fact] public void Every_heat_event_is_shown() { /* scripted run on patrol-ground (Task 15 adds it; until then a definition built in the test with traffic 6, presence 1.5, startingHeat 75), 90 s → No_event_goes_unshown's rule holds for the new events */ }
[Theory] /* WorldLoopTests per-item hash cases for each new field */
```
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~WarningTests|FullyQualifiedName~WorldLoopTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole suite.** Expected: all pass, including every frame-split test.
- [ ] **Step 5: Commit** `feat: police, roadblock and speed trap warnings; heat in the state hash`.

### Task 12: Drawing civilians and police: view, mirror and radar

**Files:**
- Modify: `App/Rendering/Neon.cs`, `VehicleRenderer.cs`, `Mirror.cs`, `Radar.cs`, `App/Diagnostics/DiagnosticsRunner.cs` (adds the `traffic` and `police` scenes described in Task 15)
- Test: snapshot (`--scene traffic,police`)

**Interfaces:**
- Produces:
  - Neon colours:
    - `CivilianCar = Pen(#7F8C99)`, `PoliceCar = Pen(#C9D6EA)`;
    - `LightBlue = #2E6BFF`, `LightRed = #FF2E4D`, with brushes;
    - `RadarCivilian`, `RadarPolice`;
    - heat colours (call 19): `HeatCalm`, `HeatWatched`, `HeatPatrol`, `HeatPursuit`, `HeatManhunt`.
  - `PenFor`: Civilian gives CivilianCar; every police kind gives PoliceCar.
  - Shapes:
    - civilians use `CivilianSedan`, `CivilianHatch` or `CivilianVan`, by `car.Id % 3`;
    - `PoliceSedan` for PolicePatrol and PoliceCruiser;
    - `InterceptorShape` (low and long);
    - `SuvShape` (tall and boxy).
  - **Light bar** on every police kind: a bar across the roof (60% of the roof width), split in two halves. The halves swap blue and red every 0.2 s of `World.Time` (`(int)(w.Time / 0.2) % 2`). `Body` and the public `DrawRear` both gain a `double time` parameter for this. Both halves glow, and the bar is drawn after the body fill.
  - A formed rolling roadblock gets a green "GAP" chevron on the road in its open lane, 10 m behind the SUVs, as parked roadblocks do (Task 13).
  - **Mirror:** police boxes get a two-colour top line that flashes the same way. Civilians are drawn in their pen.
  - **Radar:**
    - Civilian: a filled grey dot of radius 0.6·u.
    - Police: a square of half-size u, filled blue or red by the same 0.2 s swap, with the white outline; ArmoredSuv uses half-size 1.4·u.
    - The `default:` branch of the kind switch must no longer catch any kind: every new kind has an explicit case.

- [ ] **Step 1:** Build in Release and run one snapshot: `NightPursuit.exe --snapshot snapshots/p3-t12.png --seconds 6 --scene traffic,police`. View both PNGs, and in the report confirm by name: the three civilian shapes, a police light bar in both colours across the two PNGs (the scenes use different times), an interceptor and an SUV, and the mirror and radar markers.
- [ ] **Step 2: Run the whole suite** (Core unchanged). Expected: all pass.
- [ ] **Step 3: Commit** `feat: draw civilians and police with light bars in view, mirror and radar`.

### Task 13: Drawing speed traps and roadblocks

**Files:**
- Create: `App/Rendering/RoadsideRenderer.cs`
- Modify: `GameView.cs` (draw order: after the road, before the vehicles), `Radar.cs`, `HazardRenderer.cs` (skips the new types)
- Test: snapshot (`--scene roadblock`)

**Interfaces:**
- Produces:
  - **Speed trap:**
    - a roadside post 2.5 m tall with a square sign (1.2 m) showing the limit number in amber, and a small camera box;
    - drawn out to 400 m (fading in from 400 to 350 m);
    - clipped by `ClipAt` like the other hazards;
    - once triggered, the sign turns red if you were over the limit.
  - **Roadblock:**
    - each blocked lane has a barrier box (Depth 2 m, 1.1 m tall) with red and white diagonal stripes, plus a flashing light bar (the same 0.2 s swap) on top;
    - the gap lane has three green chevrons painted on the road (pointing ahead), and the word "GAP" above them while within 400 m;
    - drawn out to 450 m.
  - **Radar:**
    - a speed trap is an amber "S" at its road edge;
    - a roadblock is a red bar across its blocked lanes, with a green tick in the gap.

- [ ] **Step 1:** Build and run `--scene roadblock` (Task 15 adds it; in this task, add the scene and its `PlaceRoadblock` placement to `DiagnosticsRunner`). It shows a roadblock at 180 m with the gap on the right and a speed trap at 120 m. Confirm in the report: the stripes, the light bars, the green chevrons and "GAP", the sign number, and the radar marks.
- [ ] **Step 2: Run the whole suite.** Expected: all pass.
- [ ] **Step 3: Commit** `feat: draw speed traps and roadblocks with a marked gap`.

### Task 14: The heat meter, the busted countdown and the new warning colours

**Files:**
- Create: `App/Rendering/HeatMeterView.cs`
- Modify: `Hud.cs` (`ReadoutBounds` height 156 → 200), `Overlays.cs` (`DrawBusted`, `WarningBrush`, help line), `GameView.cs`, `App/Diagnostics/DiagnosticsRunner.cs` (adds the `busted` scene described in Task 15)
- Test: snapshot (`--scene busted`)

**Interfaces:**
- Consumes: `World.Heat`, `World.Busted` (Tasks 2 and 10).
- Produces:
  - `static class HeatMeterView { static Rect Bounds(HudLayout l); static void Draw(DrawingContext dc, HudLayout l, World w); }`.
    - Bounds: `Rect(Width − 214K, 158K, 198K, 38K)`, inside the grown `ReadoutBounds`.
    - It draws the label "HEAT", the value as a number ("HEAT 72"), and the level name in its level colour (call 19), right-aligned on the same line.
    - Under the label is a 198K × 10K bar with five segments in the level colours, filled to `Value / 100`, with tick marks at 25, 50, 70 and 90.
    - While `RiseFlash > 0`, the outline is drawn bright and thick, blinking at 2 Hz (kept under 3 flashes a second).
    - Check the arithmetic in the report: the right edge arrow (D22: y ≈ 388 at 720p) and the NO GRIP / PUNCTURED rows (which end at about 152) must stay clear.
  - `Overlays.DrawBusted(dc, l, w)` runs while `w.Busted.Counting`:
    - a centred red title `$"BUSTED IN {SecondsLeft}"` at 44 pt, at y = 0.70·H, with "SPEED UP TO BREAK FREE" (18 pt, amber) under it;
    - a black backing box so it reads over the road;
    - it runs after the announcement, so nothing covers it;
    - check in the report that it stays clear of the OFF ROAD text at 0.62·H and the hood at 0.86·H.
  - `WarningBrush`: PoliceAlongside and PoliceBehind use `LightBlue`; RoadblockAhead uses red; SpeedTrapAhead uses amber.
  - Help line added after GAME: `"HEAT       hurting civilians or police raises heat; drive clean to cool off"`. Recheck the D66 help box arithmetic at 1280×720 in the report.

- [ ] **Step 1:** Build and run one snapshot, `--scene busted,police,roadblock`. Confirm:
  - the meter at Pursuit with its colour and name;
  - the countdown text clear of everything;
  - the police warnings in blue, and the roadblock warning with its arrow.
- [ ] **Step 2: Run the whole suite.** Expected: all pass.
- [ ] **Step 3: Commit** `feat: heat meter, busted countdown and police warning colours`.

### Task 15: Content, scenes, stress and performance

**Files:**
- Modify: `content/missions/mission-01.json` (`"traffic": { "carsPerKm": 3 }, "police": { "presence": 1 }`), `App/Diagnostics/DiagnosticsRunner.cs`
- Create: `content/missions/patrol-ground.json` (call 21)
- Test: `MissionTests` (`Patrol_ground_loads`), stress and perf

**Interfaces:**
- Produces:
  - `patrol-ground.json`:
    - id "patrol-ground", title "Patrol Ground", an original briefing;
    - road seed 33, approachLength 5000;
    - loadout `{oil 5, smoke 3, caltrops 3, missiles 2, strikes 1}`;
    - traffic 6; police presence 1.5, startingHeat 75, so the owner meets cruisers at once and patrols, traps and roadblocks as it cools;
    - waves: Runner ×2 at 800, Runner ×2 at 2500;
    - one crate of each item between 1000 and 4000;
    - boss "Night Heron" (original), health 120, phases Blocker → 0.5, Runner → 0.
  - Scenes (each added by the task that first needs it; listed here as the full set):
    - `traffic` (Task 12): 12 civilians across all three shapes, at 30–400 m.
    - `police` (Task 12): heat 80; a Front and a Side box-in around the player, an interceptor alongside, and the SUV pair 40 m ahead.
    - `roadblock` (Task 13).
    - `busted` (Task 14): heat 80, the player stopped, a cruiser 8 m behind, 1.4 s held.
  - `--stress`:
    - the 13-car list now cycles over every non-boss kind, explicitly listed rather than cast with `% 6`;
    - each frame adds a speed trap and a roadblock (gap lane `frame % 3`);
    - heat cycles 0–100 by `frame`;
    - every 4th frame sets `Busted` counting with 1.2 s held.
  - `--perf`: 25 cars across all non-boss kinds, plus 8 extra civilians and 3 police, heat 80, a roadblock at 300 m and a speed trap at 200 m. The report line names the counts.

- [ ] **Step 1: Write the test** `Patrol_ground_loads` (traffic 6, presence 1.5, startingHeat 75, boss "Night Heron" with 2 phases) and run it. Expected: FAIL, then PASS once the file exists.
- [ ] **Step 2: Build; run the snapshot** `--scene traffic,police,roadblock,busted` once, **then `--stress` and `--perf`.** Expected: stress 0 failures over every mission, including patrol-ground, at all sizes and 0×0; perf mean `OnRender` under 8 ms.
- [ ] **Step 3: Run the whole suite.** Expected: all pass. mission-01's own determinism and frame-split tests still pass, because they compare runs with each other.
- [ ] **Step 4: Commit** `feat: traffic and police in mission 1, the patrol ground, scenes, stress and perf`.

### Task 16: Decisions log and the owner's checks

**Files:**
- Modify: `docs/decisions.md`

- [ ] **Step 1:** Add "## Plan 3 calls (before building)", with D69 onward for calls 1–24, in the owner's plain style. Each entry gives "Cost to change". Check every number against the code as built before writing it.
- [ ] **Step 2:** Add "## While building Plan 3" for the rulings made during the build, and update "Open items". Plan 1's "mission 1 is fairly sparse" item is now partly answered by traffic, so say how.
- [ ] **Step 3:** Add "## Checks only you can do (Plan 3)":
  1. Drive mission 1 and judge the traffic density and how civilians move aside and pull over.
  2. On the patrol ground (`--mission patrol-ground`), raise heat to Pursuit and then Manhunt:
     - get boxed in and break out by braking;
     - shake the police with smoke;
     - take the gap in a roadblock;
     - pass a speed trap over and under its limit;
     - get busted once on purpose.
  3. Check that everything reads without sound: the heat meter and its flash, the banners, the countdown, and the police light bars (both colours visible).
  4. Judge whether the heat numbers (HT-2) feel fair.
- [ ] **Step 4: Commit** `docs: plan 3 decisions and the owner's checks`.

---

## Spec coverage for this plan

| Spec | Task |
|---|---|
| CIV-1 traffic speeds, lanes, reactions | 4, 5 |
| CIV-2 neutral colour, hostiles stand out | 12 |
| CIV-3 heat (and armor damage) for hurting civilians; the owner ruled out point penalties | 3 |
| HT-1 meter, five levels, name, colour, flash | 2, 14 |
| HT-2 every heat source and decay | 2, 3, 6 |
| HT-3 police never targets, no points, behaviour by level | 1, 3, 7, 8, 9 |
| HT-4 light bars, trap limit shown, roadblocks visible from 300 m | 12, 13 (400 m) |
| HT-5 heat per mission | 4 (`MissionRun.Rebuild` builds a new `World`) |
| HT-6 busted with countdown and cancel | 10, 14 |
| HT-7 always a way out | 6, 8, 9 |
| HUD-1 heat meter | 14 |
| HUD-3 radar colour codes civilians and police | 12, 13 |
| HUD-4 roadblock ahead warning, heat events as messages | 3, 11 |
| §7.4 HeatMeter and PoliceDispatcher, table in one place | 2, 7 |
| §7.6 events for later sound | 2, 3, 6, 10 |
| §11 "three civilian hits reach Patrol", "strike spares civilians", busted, gap guarantee, escalation | 1, 3, 6, 7–10 |
| NFR-1/5 performance and stress with traffic and police | 15 |

Not in this plan:
- **Plan 4:** missions 2–6 get their own traffic and police settings; the briefing screen shows them; the text-size setting.
- **After v1 (§10):** police helicopters and enemy spies.
