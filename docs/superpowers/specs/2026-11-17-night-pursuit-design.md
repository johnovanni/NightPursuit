# Night Pursuit: design

*Working title. Requirements and architecture for version 1. Written 2026-11-17.*

## 1. Intent

Night Pursuit is a midnight car-combat arcade game for Windows. You drive a weaponised car down a
glowing vector road, seen from the driver's seat, hunting enemy agents through civilian traffic.
Each mission ends with a boss car. The mechanics are inspired by Spy Hunter (road combat, rear-dropped
weapons, civilians you must not hurt) and Speed Freak (first-person vector road at speed), but it is
an **original game**.

**The heart of it:** *cat-and-mouse fighting, with speed as the thrill.* Most of the polish goes into
how enemies hunt you and how you outwit them: reading the mirror and radar, timing an oil slick,
choosing whether to shoot (a wreck blocks the road) or ram. Speed is what makes those choices tense.

**Who it's for:** the owner, who is hard of hearing. **Nothing important may depend on sound.**
Music and sound effects come later and only ever add to what the screen already shows.

**Prototype so far:** `spikes/ControlsLab` (throwaway) settled the controls and the look. Its
lessons are folded in here. The real game is a fresh build and does not reuse that code.

## 2. Goals, non-goals, anti-goals

**Goals (v1)**
- A complete arcade loop: title screen → mission briefing → mission → boss → next mission, with score and high scores.
- Combat that rewards reading the situation: enemies of rising difficulty, civilians in the way,
  and police who grow more dangerous as you misbehave (the heat system).
- A fast, smooth, readable vector look at 60 frames per second.
- Clean object-oriented architecture with the game rules in a library that has thorough automated tests.

**Non-goals (v1)**
- Music and sound effects. The design only leaves room for them (section 9).
- Enemy spies and police helicopters. They come in later missions after v1; the design must allow them (section 10).
- Multiplayer, online features, a track editor, or mod support.
- Platforms other than Windows desktop.

**Anti-goals: things that would make it a failure even if it works**
- Looking or sounding like a copy: no names, characters, car designs, logos, music or level layouts
  from Spy Hunter, Speed Freak or any other game.
- Any warning, hit or event that is only audible, or that can kill you without first showing up on screen.
- Deaths that feel random, such as being hit by something you had no way to see coming.
- Game rules tangled into WPF code so they can't be tested.

## 3. Constraints

- C# on .NET 10, WPF, Windows 11. Same layout as the owner's other apps: `src/NightPursuit.Core`,
  `src/NightPursuit.App`, `tests/NightPursuit.Core.Tests` (xUnit), `Directory.Build.props`
  with Nullable and ImplicitUsings enabled.
- Input: keyboard, mouse wheel, and an Xbox-style gamepad through XInput (`xinput1_4.dll`).
- **No screen-capture scripts** (the antivirus flags them). For visual checks, the app renders
  itself to PNG through command-line diagnostic options (section 8.4).
- Local git repository. Committing and pushing need the owner's go-ahead. There is no GitHub repo yet.
- Accuracy matters to the owner. The physics is arcade physics, but it must be internally
  consistent: units are metres, seconds, m/s, and the HUD shows mph.

## 4. Approaches considered

| Approach | Trade-off | Decision |
|---|---|---|
| **1. Classic OO.** A `Vehicle` class hierarchy with pluggable drivers (AI) and weapon loadouts; rules in a WPF-free Core library | Readable, matches the request for object orientation; risk of a rigid hierarchy, reduced by putting behaviour in drivers and loadouts rather than subclasses | **Chosen by the owner** |
| 2. Entity–component–system | Very flexible mixing of traits; not really object-oriented, harder to follow, more than this game needs | Rejected |
| 3. One WPF control per car (shapes and bindings) | Idiomatic WPF, but too slow for hundreds of glowing lines at 60 fps | Rejected |

## 5. Requirements

Each requirement has an ID so tests and the plan can refer to it. "Shall" means it is required for v1.

### 5.1 Driving

- **DRV-1** The view is first-person from the driver's seat: 70° horizontal field of view, eye
  1.15 m above the road, with the hood and twin gun barrels drawn at the bottom of the screen.
- **DRV-2** The road has three 5 m lanes, with bends (radius 180–700 m), hills, and road edges,
  lane dashes, roadside posts and street lamps drawn as glowing lines on black. Road geometry ahead
  and behind is computed exactly (heading integrated along the road), so long tight bends are drawn
  correctly and hills hide what lies beyond them.
- **DRV-3** Throttle schemes, chosen in Settings: **Ramp pedal with cruise control** (default) and
  **Speed lever**. The gamepad's analog triggers work as gas and brake in either scheme.
  The prototype's gearbox and scroll-wheel schemes are dropped.
- **DRV-4 The car turns to aim.** Steering turns the car's nose: it changes the heading relative
  to the road, up to 20°, and the car moves the way it points (sideways speed = speed × sin heading).
  Steering builds up while a key is held and returns to centre when it is released. The gamepad
  stick steers in proportion to how far it is pushed. Steering gets lighter at high speed, as an option.
  A straightening assist always turns the nose back toward the road's direction (8 per second), even
  while a key is held, so a held key settles the nose at a steady angle and letting go stops the slide
  within about 0.4 s. In the prototype the assist worked only with no key held and was weaker (3 per
  second): after letting go, the car slid on for over a second and overshot its lane by about 3 m, so
  players weaved, and because the camera turns with the nose the whole view swung side to side (the
  owner saw it as a sideways jitter after gun kills). The always-on assist with a faster nose (90°/s)
  keeps lane changes as quick and cut the view's swing by 2.6× (prototype `--steer-test`).
  The nose limit shrinks with speed so the car never slides sideways faster than about 10 m/s
  (20° at low speed, about 8° at top speed). Without this, a held key at speed swung the car across
  the road in about a second and over the edge. That was found in the prototype as an off-road
  "jitter", and the steering trace measured it (lane-keeping off-road time went from 7.4 s to 0).
- **DRV-4a** Leaving the road shakes the view and always shows a large "OFF ROAD" banner with an
  arrow pointing to the side you went off, so the shake has a visible cause.
- **DRV-5** Speed model: pull falls off toward top speed (default 155 mph, so there is no hard cap),
  coasting slows the car gently, brakes are strong, hills slow you climbing and speed you descending,
  and leaving the road slows and shakes the car. On a bend the car keeps its heading unless you
  steer, so it drifts toward the outside; the straightening assist limits the drift.
- **DRV-6** The camera turns with the car, so the screen centre is where the nose points.
- **DRV-7** Default feel values come from the prototype, including the turn-to-aim steering values
  still to be settled there. The F12 tuning panel exists in **debug builds only**.

### 5.2 Weapons

All weapons have on-screen readouts (ammo or charges, and cooldown).

- **WPN-1 Machine guns.** Twin guns fire tracers **straight along the car's heading**: you aim
  by turning the nose (DRV-4), and a small aim mark at the screen centre shows the gun line. On a bend,
  tracers leave the road unless you turn in. On tight bends, distant targets are hard to hit on
  purpose: that is where oil, caltrops and ramming come in. (In the prototype the tracers followed
  the road.) Ammunition is unlimited.
  A heat gauge overheats the guns after about 4 s of continuous fire and locks them for 2 s.
- **WPN-2 Oil slick.** Dropped behind you. Cars that drive over it spin. Charges recharge over time.
- **WPN-3 Smoke screen.** A cloud behind you lasting a few seconds. AI drivers inside it lose sight
  of you, wander, and slow down; cars may collide inside it.
- **WPN-4 Caltrops.** Scattered behind you across about one lane. Cars that cross them get punctured:
  slower, wobbly steering, and they eventually stop.
- **WPN-5 Missile.** Rare. Fires forward and locks onto the nearest hostile ahead within range and
  view. It destroys most cars outright and badly damages the boss.
- **WPN-6 Satellite strike.** Very rare, at most one held at a time. After a 1.5 s on-screen
  targeting sweep, it destroys every **hostile** car within 400 m ahead and 150 m behind.
  Civilians and police are spared (it is a precision strike). Its wrecks block the road like gun wrecks.
- **WPN-7 Ramming.** Steering hard into the side of a car knocks it into a spin, as in the prototype.
  A spinning car that leaves the road crashes. Ramming costs you some armor.
- **WPN-8 Supply.** Each mission starts with a loadout given in its briefing. Supply crates appear
  on the road during a mission, drawn as glowing boxes in a lane. Driving through one restocks a
  named item, and a label shows what it holds.

### 5.3 Wrecks and hazards

- **WRK-1** A car destroyed by **gunfire, missile or satellite strike** becomes a burning wreck
  that stays in its lane and blocks it for anyone behind: the player, enemies, civilians and police.
- **WRK-2** A car that **runs off the road** (spin, ram, puncture) crashes beside the road and does not block it.
- **WRK-3** Hitting a wreck does heavy damage and stops you hard. Wrecks show on radar as an X.
  They are cleared once they are 600 m behind you.
- **WRK-4** Enemy AI swerves to avoid wrecks it can see, with a skill-dependent reaction time,
  so "destroy one in front of a pursuer" is a real tactic.

### 5.4 Enemies (hostile agents)

Each mission draws from these types, in rising difficulty, then ends with a boss.

| Type | Behaviour | Toughness |
|---|---|---|
| **Runner** | Drives and changes lanes; swerves when shot at | weak |
| **Blocker** | Truck; moves into your lane and brake-checks you | tough |
| **Pursuer** | Comes up from behind, tries to sideswipe you, backs off, repeats | medium |
| **Gunner** | Gets ahead of you and fires back through its rear window | medium |
| **Dropper** | Drops its own oil and caltrops in front of you | medium |
| **Armored** | Bullets do little; needs a missile, a ram off the road, or a hazard | very tough |
| **Boss** | One per mission, with a health bar and 2–3 attack phases drawn from the behaviours above | boss |

- **ENM-1** Enemies are hostile and worth points. They are drawn in a distinct colour per type and
  shown on radar in that colour.
- **ENM-2** Each behaviour above is a separate driver class (section 7.2), so a boss phase can reuse them.
- **ENM-3 Boss damage.** Nothing ends a boss fight in one blow. Gunfire chips away at its health,
  and each other weapon does a fixed amount instead of its usual effect:
  - missile: 20% of full health;
  - satellite strike: 35%;
  - oil slick: a 1.5 s spin that also opens its armor (gunfire does double damage while it spins), but it never leaves the road;
  - caltrops: slow it for 4 s;
  - ramming: pushes it aside, never off the road;
  - hitting a wreck: 10%, then it swerves round.
  
  A boss phase ends when its health falls below that phase's threshold.
- **ENM-4 Enemy missiles.** In missions 5 and 6 only, one boss phase fires slow homing missiles at you.
  Each is announced by an on-screen "MISSILE INCOMING" banner and a side arrow at least 2 s before
  impact. It can be dodged by changing lanes in the last second, or shot down with the guns.

### 5.5 Civilians

- **CIV-1** Civilian traffic drives at normal speeds (25–35 m/s), keeps to lanes, and reacts:
  slows down, pulls over, or swerves away from danger.
- **CIV-2** Civilians are drawn in a calm, neutral colour. Hostiles stand out from them.
- **CIV-3** Damaging or destroying a civilian costs points and raises heat (section 5.6).

### 5.6 Police and heat

- **HT-1** A heat meter from 0 to 100 is always on screen, in five levels. The level name and colour
  are shown, and the meter flashes when it rises.

| Level | Heat | Police |
|---|---|---|
| Calm | 0–24 | none |
| Watched | 25–49 | none yet. The meter warns you |
| **Patrol** | 50–69 | **neutral hazards**: patrol cars in traffic, speed traps, roadblocks |
| **Pursuit** | 70–89 | police cruisers chase you and try to box you in and slow you |
| **Manhunt** | 90–100 | faster interceptors and armored SUVs ram you and set up rolling roadblocks |

- **HT-2** Heat rises when you: hit a civilian (+10) or destroy one (+25); hit a police car (+15);
  pass a speed trap above its posted limit (+8); hit a roadblock (+15); destroy a police car (+40);
  fire on a police car (+5 per hit). Heat falls slowly while you drive cleanly (−2 per s, after 5 s
  without an offence), and faster when no police car is within 300 m.
- **HT-3** Police are never hostile targets. Shooting or ramming them always raises heat, and they
  give no points. At Patrol level they just drive, and speed traps and roadblocks just sit there.
  From Pursuit up, they actively chase you.
- **HT-4** Police cars use flashing light bars drawn in blue and red, unlike any enemy. Speed traps
  show their limit, and roadblocks are visible from at least 300 m.
- **HT-5** Heat carries through a mission and resets at the start of the next one.
- **HT-6 Busted.** From Pursuit level up, if you are held below 5 mph with a police car within 15 m
  for 3 s, you are busted and lose a spare car (as if wrecked). The countdown shows on screen from
  the first moment, as a large "BUSTED IN 3… 2… 1" banner. Breaking free (speeding back up above
  5 mph) cancels it.
- **HT-7 Always a way out.** Roadblocks always leave one gap (a lane or the shoulder), visible and
  marked from at least 300 m. Box-ins can always be broken by braking, swerving, or ramming
  (ramming raises heat). Police never block all three lanes with moving cars.

### 5.7 Missions

- **MSN-1** The game is a sequence of 6 missions. Each one has: a briefing screen (story text, the
  boss's name and a drawing of it, your starting loadout), a road (generated from a seed plus a
  bend/hill profile), traffic and police settings, waves of enemies, and a boss.
- **MSN-2** A mission has two parts. First you **survive the approach**: a set distance of road with
  enemy waves and traffic. Then **the boss appears**, and destroying it completes the mission.
- **MSN-3** You have armor (100) and 3 spare cars per game. Losing all armor, or being busted (HT-6),
  uses a spare. You restart a short way back with full armor and the boss's health unchanged. With no
  spares left, the game is over.
- **MSN-4** Missions are described in data files (`content/missions/*.json`) so they can be changed
  without code changes: enemy types per wave, spawn distances, traffic density, police presence,
  supply crates, boss definition.
- **MSN-5** Score: points per enemy by type, a bonus per mission, and penalties for civilians.
  The top 10 high scores are saved with initials and the mission reached.

### 5.8 Screens and HUD

- **HUD-1** In play: speed (mph), throttle and brake bars, a target-speed marker (cruise or lever),
  armor bar, spare cars, weapon readouts, heat meter, score, mission progress (distance to the boss),
  and the boss health bar when the boss is on screen.
- **HUD-2** Rear-view mirror at the top centre, showing the road and cars behind, with headlights.
- **HUD-3** Radar in a corner: top-down road shape, every car colour-coded (hostile type, civilian,
  police), wrecks, slicks, caltrops, crates.
- **HUD-4** Visual warnings: a flashing text banner with a side arrow at the screen edge for a hostile
  closing from behind, for a blocker or roadblock ahead, and for an incoming missile. A red edge flash
  when you're hit. Every scoring or heat event shows as a short text message.
- **HUD-5** Screens: Title, Briefing, Play, Pause (with Settings), Mission complete, Game over,
  High scores, Settings (controls scheme, key bindings view, lighter steering, screen shake on/off,
  text size).

### 5.9 Non-functional

- **NFR-1** 60 fps on the owner's PC at 1280×720 and full-screen 1920×1080, with the worst case
  on screen (about 25 vehicles, hazards and effects).
- **NFR-2** Simulation runs at a fixed 120 steps per second, independent of frame rate.
  It is deterministic for a given seed and input sequence.
- **NFR-3** Accessibility: no information is carried by sound alone (see the anti-goals). Colour is
  never the only cue: hostile types also differ in shape, and police have light bars.
  Text has a size option.
- **NFR-4** Settings and high scores are saved as JSON in `%AppData%\NightPursuit`.
- **NFR-5** The game never crashes on drawing edge cases. A stress diagnostic renders every
  10 m of each mission's road (section 8.4).

## 6. Architecture overview

```
NightPursuit.App (WPF)                 NightPursuit.Core (no WPF)
 ├ Screens (Title, Briefing, Play…)     ├ Session: Game, Mission, MissionDirector, Score, HighScores
 ├ GameView : FrameworkElement          ├ World: Track, RoadView, World, Clock, Rng
 │   ├ Projector (camera)               ├ Vehicles: Vehicle + subclasses, Drivers, Damage
 │   ├ RoadRenderer, SkyRenderer        ├ Weapons: Weapon + subclasses, Loadout, Projectiles
 │   ├ VehicleRenderer per kind         ├ Hazards: OilSlick, SmokeCloud, Caltrops, Wreck,
 │   ├ HazardRenderer, EffectRenderer   │          SpeedTrap, Roadblock, SupplyCrate
 │   └ Hud, Mirror, Radar               ├ Police: HeatMeter, PoliceDispatcher
 ├ Input: KeyboardReader, XInputPad     ├ Physics: CarPhysics, Collisions
 ├ GameLoop (CompositionTarget)         ├ Control: ControlFrame, IThrottleScheme (Ramp, Lever)
 └ Diagnostics (--snapshot, --stress)   ├ Events: GameEvent stream (for HUD messages, later sound)
                                        └ Content: MissionDefinition (JSON), loader + validator
```

**Dependency rule:** App depends on Core. Core depends on nothing outside .NET. Core never calls
WPF, the clock or `Random` directly; time comes in as `dt`, and randomness comes from a seeded `IRng`.

## 7. Core design

### 7.1 World and loop

- `World` owns the track, all vehicles, projectiles, hazards and effects, plus `HeatMeter` and the
  event list. `World.Step(ControlFrame, dt)` is the only way the simulation advances.
  The App calls it at 120 Hz in fixed steps, carrying any leftover time to the next frame.
- `Track` holds curvature and height per 5 m segment (built from a seed and the mission's profile).
  `RoadView` gives positions ahead and behind in the player's frame
  (`At(distance, lateral) → (X, Forward, Y, Heading)`), using exact heading integration as the prototype ended up doing.
- Everything moving uses **road coordinates**: `Distance` along the road, `Lateral` metres right
  of centre, and `Heading` relative to the road's direction at that point. To move something
  straight across the ground, advance it along its heading and subtract the road's turning
  (curvature × distance moved) from its relative heading. The player car, tracers and missiles all
  move this way. That is how tracers fly straight and leave the road on a bend, and it means hit
  tests never need a second coordinate system.

### 7.2 Vehicles

```
abstract Vehicle            road position, speed, lateral motion, heading offset, armor,
                            state (Driving, Spinning, Punctured, Crashed, Wrecked), size
 ├ PlayerCar                + Loadout, spare cars
 ├ AgentCar                 hostile; Kind (Runner, Blocker, Pursuer, Gunner, Dropper, Armored)
 │  └ BossCar               + phases, each a driver + weapons + armor threshold
 ├ CivilianCar
 └ PoliceCar                Kind (Patrol, Cruiser, Interceptor, ArmoredSuv)
```

- **Behaviour lives in drivers, not subclasses.** `IDriver.Decide(Vehicle self, WorldView view) →
  DriveIntent` (target speed, target lateral position, fire/drop requests). Implementations:
  `PlayerDriver` (wraps the throttle scheme and steering), `RunnerDriver`, `BlockerDriver`,
  `PursuerDriver`, `GunnerDriver`, `DropperDriver`, `CivilianDriver`, `PatrolDriver`, `ChaseDriver`,
  `BoxInDriver`, `RollingRoadblockDriver`. Drivers can be swapped at run time, which is how boss
  phases and police escalation work.
- `WorldView` is a read-only, filtered view of the world. It is what a driver "sees", so smoke can
  hide the player from a driver.
- `CarPhysics` is shared by all vehicles: the same speed model and turn-to-aim steering model
  (heading, yaw rate, straightening assist, 20° limit), with per-type stats (top speed,
  acceleration, yaw rate, mass) from a `VehicleStats` record. AI cars use the same steering, through
  their drivers' target lateral position.
- `Collisions` resolves car-to-car contact (rear-end, sideswipe, spin when rammed hard), car-to-wreck,
  and car-to-hazard, using the prototype's overlap rules extended to all pairs.
  It uses a broad phase sorted by road distance, so it scales to about 25 vehicles.

### 7.3 Weapons

```
abstract Weapon      name, charges/ammo, cooldown, recharge, Fire(owner, world)
 ├ MachineGuns       overheat gauge; spawns Tracer (straight-flying)
 ├ OilDropper        spawns OilSlick
 ├ SmokeLauncher     spawns SmokeCloud
 ├ CaltropDropper    spawns Caltrops
 ├ MissileLauncher   spawns Missile (homing)
 └ SatelliteStrike   targeting sweep, then wrecks every hostile in range
Loadout              ordered weapons + selection; restocked by SupplyCrate
```

Enemies reuse the same classes: a Gunner has `MachineGuns` firing backwards, and a Dropper has
`OilDropper` and `CaltropDropper`.

### 7.4 Heat and police

- `HeatMeter` turns offences (as `GameEvent`s) into heat, applies decay, and reports the level.
  The table in HT-1 and HT-2 lives in one place as data.
- `PoliceDispatcher` watches the level. At **Patrol** it adds patrol cars into traffic and places
  speed traps and roadblocks ahead. At **Pursuit** and **Manhunt** it switches existing police
  drivers to chase or box-in behaviour and spawns tougher vehicles. When heat drops, police fall
  back to patrol behaviour and drop away.

### 7.5 Missions and session

- `MissionDefinition` (loaded from JSON and validated with clear error messages) holds the road
  seed and profile, approach length, waves (distance, types, counts), traffic density, police
  presence, crate schedule, starting loadout, and the boss (stats, phases, briefing text).
- `MissionDirector` spawns waves and traffic by distance travelled, starts the boss phase, and
  decides when the mission is won or failed.
- `Game` runs the session across missions: score, spare cars, high-score entry.

### 7.6 Events

Every notable thing (hit, wreck, heat change, crate collected, boss phase, warning) is published as
a `GameEvent` record. The HUD turns these into on-screen messages and flashes. Later, sound and music
subscribe to the same events, so audio never carries information the screen lacks.

## 8. App design

### 8.1 Game loop
`CompositionTarget.Rendering` drives the loop: read input → step the world in fixed 120 Hz steps →
invalidate `GameView`. It skips duplicate Rendering calls within one frame, as the prototype does.

### 8.2 Rendering
- `GameView.OnRender` draws everything with `DrawingContext`. Lines are batched into frozen
  `StreamGeometry` objects per pen. The glow effect is a wide faint stroke under a thin bright one.
  Filled black car bodies hide the road lines behind them.
- `Projector` maps road-frame points to the screen. Hill occlusion uses the prototype's
  crest-clipping, with its fixes: only road still heading away can hide things, and the hiding line
  never goes above the top of the screen.
- One renderer class per thing drawn (`RoadRenderer`, `VehicleRenderer` with a shape per vehicle
  kind, `HazardRenderer`, `EffectRenderer`, `Hud`, `Mirror`, `Radar`).
- Screens other than Play are ordinary WPF views (XAML) styled in the same neon-on-black look.

### 8.3 Input
`KeyboardReader` and `XInputPad` produce a `ControlFrame` (held controls) and `GameAction`s (one-off presses).
Default keys are the prototype's: arrows or WASD to drive, Space for guns, X or Ctrl for the
selected rear weapon, Tab or Shift to cycle rear weapons (D68), F for missile, V for satellite strike,
C for cruise, P or Esc to pause. The gamepad mapping is in the help screen.

### 8.4 Diagnostics (for checking without screen capture)
- `--snapshot out.png --mission N --seconds S [--scenario name]` simulates on autopilot and renders one frame.
- `--stress report.txt [--mission N]` renders every 10 m of the mission's road with many objects
  and reports any exception (exit code 1).
- `--perf` runs a scripted heavy scene and reports average render time per frame.

## 9. Sound (later)
v1 has no sound. Later work adds spy and detective themed music and effects, original or properly
licensed, subscribed to `GameEvent`s, with a mute option. Before anything audible is added, it must
be checked against NFR-3.

## 10. Extension points for later content
- **Enemy spies:** a new `AgentCar` kind whose driver behaves like a civilian (and is drawn as one)
  until it is close, then reveals itself. It needs a "reveal" visual cue and must stay fair under NFR-3.
- **Police helicopters:** a new non-road `Aircraft` base class next to `Vehicle`, with a spotlight
  that raises heat while you're in it. Drawn above the road and on radar. It is triggered at
  Manhunt level in later missions.

## 11. Testing strategy
- **Unit tests (Core):** physics (top speed reached, coasting, braking distance, hill effect),
  throttle schemes, steering ramps, `RoadView` geometry (a straight road, a constant bend matching
  the circle formula, symmetry ahead and behind), collisions (each contact type), each weapon,
  wreck vs crash rules (WRK-1, WRK-2), heat gains and decay and level thresholds (HT-1, HT-2),
  police escalation, busted countdown and its cancel (HT-6), roadblock gap guarantee (HT-7),
  boss damage table (ENM-3), mission definition loading and validation.
- **Steering and aim tests:** with no input on a constant bend, the car settles at the predicted drift;
  a tracer fired on a straight stays on its line; a tracer fired on a bend with zero heading leaves
  the road on the outside at the distance predicted by the circle geometry.
- **Scenario tests:** seeded worlds with scripted inputs and asserted outcomes. For example: "oil
  dropped in front of a pursuer makes it spin", "gun kill leaves a wreck in lane 2", "three civilian
  hits reach Patrol level", "satellite strike spares civilians".
- **Determinism test:** the same seed and inputs give an identical world state hash after 60 s.
- **App checks:** the snapshot and stress diagnostics, run as part of the verification steps.
  The renderer is not unit-tested beyond the projector maths.

## 12. Calls made (open to change)

These were filled in to make the design complete. The owner hasn't decided them yet:

1. Six missions in v1, each "approach, then boss". Three spare cars per game.
2. Weapons come from the mission loadout plus supply crates on the road. (Deliberately not a
   supply truck you drive into, which is Spy Hunter's signature.)
3. Machine guns have unlimited ammunition but overheat.
4. The satellite strike spares civilians and police.
5. Heat numbers in HT-2 are starting values to be tuned in play.
6. Gearbox and scroll-wheel throttle schemes are dropped. The F12 tuning panel is kept for debug builds.
7. Heat resets between missions.
8. Bosses can't be killed in one blow (ENM-3), and only the last two bosses fire missiles (ENM-4).
   (These came from the reviewer check.)

**Decided by the owner after the reviewer check:** the car turns to aim (DRV-4, DRV-6), and being
busted by police costs a spare car, with a way out always guaranteed (HT-6, HT-7).

## 13. Left to the builder
Class-level details beyond those named here, file layout inside each project, the JSON schema's
exact shape, story and boss names (all original), colours within the neon-on-black palette, and
balance numbers not given above.
