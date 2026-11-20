# Night Pursuit Line-Art Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redraw the whole game in the owner's chosen minimalist 1980s line art ("Combined"): only the edges that face
you, a heavy outline over light detail, breaks where light catches, a soft glow, and lines that fade with distance.

**Architecture:** The trial on branch `spike-line-art` already restyled the cars, road and mountains, and added the
windshield cracks (with `PlayerCar.WorstDamage` and its tests) and the rolling tyre tread. This plan:
- turns that trial into the game's only look, by removing the style switch and the old drawing paths;
- moves everything else that is drawn onto the same `LineArt` pens: wrecks, hazards, the arsenal, the roadside,
  effects, the hood, the mirror and the HUD frames.
All the work is in the App's `Rendering/` folder and the diagnostics. Core is not touched.

**Tech Stack:** C# / .NET 10, WPF (`net10.0-windows`), xUnit 2.9.3 (Core only).

**Spec:** `docs/superpowers/specs/2026-11-20-line-art-restyle-design.md` (the owner's choices, goals 1 to 8, the
non-goals, the anti-goals and my calls). It sits on the game design,
`docs/superpowers/specs/2026-11-17-night-pursuit-design.md`; NFR-3 (never by colour alone) binds every task.
Also read `docs/decisions.md`: D116 (stress-test length) and "Open items after Plan 3".

**Where this sits:** after Plan 3 and before Plan 4. Plan 4 is written but not built; Task 7 here updates it to build on
this branch.

## Global Constraints

- **Frameworks:** App targets `net10.0-windows` with `<UseWPF>true</UseWPF>`. No new NuGet packages.
  `Directory.Build.props` is unchanged.
- **Core is not changed.** Core already has `PlayerCar.WorstDamage` from the trial. StateHash is unchanged, and all
  873 tests keep passing with no test edited.
- **One look.** After Task 1 there is no style switch. `LineArt` always draws the Combined look, and no `Classic`,
  `Laser` or `NewWave` code is left.
- **Combined pen values** (from the trial; use them verbatim). The weight is `w = 0.45 + 0.55·f` and the fade level
  `f = level/12`, where `level = clamp(round(fade·12), 1, 12)`:
  - Outline: width `2.3·w`, alpha `f`.
  - Detail: width `1.0·w`, alpha `0.7·f`.
  - Scenery: width `1.6·w`, alpha `f`.
  - The width is never under `0.7` px.
  - Each pen has a halo of alpha `0.08·alpha`, width `width + 9·w`, and a glow of alpha `0.24·alpha`, width `width + 3.5·w`.
  - All three have round caps and joins.
  - `LineArt.Fade(ahead) = clamp(1 / (1 + (max(0, ahead − 20)/260)^1.5), 0.22, 1)`.
- **Presenting edges only.**
  - Things standing on the road have no stroke along the road.
  - Black fills hide what is behind.
  - Breaks: a gap past the right shoulder of an outline and a small one low on the left (light from the upper right).
    Outlines only, never detail or text.
- **Every gameplay signal keeps its shape (NFR-3).** A restyle changes line weight, glow and fading only. Each of these
  still reads by shape, not just colour:
  - a hostile oil slick or caltrops set, with its red dashed outline;
  - the Gunner aiming or firing;
  - a Dropper chute arming;
  - the police light bars;
  - the boss shield pulse and open armour;
  - a car's hit flash and its armour bar;
  - the GAP chevrons and word, a roadblock's stripes and lights, and a speed trap's sign and CAUGHT;
  - the missile lock box, and the enemy-missile ring and warning.
- **HUD text is untouched:** same strings, sizes, colours and positions. Only frames and gauge outlines change pens.
- **No wreck "breakdown" effects** (the owner dropped them; design non-goals).
- **No screen-capture scripts.** Check visually with `--snapshot`, `--perf` and `--stress` only.
  - One `--snapshot` launch per task, with all of that task's scenes in that one run (Task 7 may use two, one per size).
  - If the permission system refuses a launch, do not retry; report what is left unseen.
- **Paths:**
  - Build with `dotnet build src/NightPursuit.App -c Release`. The exe is
    `src/NightPursuit.App/bin/Release/net10.0-windows/NightPursuit.exe`.
  - Snapshots go in `snapshots/` (git-ignored): `snapshots/before/` from Task 1, `snapshots/after/` from each task.
  - Before/after comparison sheets are made with a throwaway Python + PIL script kept outside the repo. Do not run it
    with `python -I`, which hides PIL.
- **Commits:**
  - local commits on a new branch `line-art`, created from `spike-line-art`;
  - messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`;
  - never push.
- **Test command** (from the repo root): `dotnet test tests/NightPursuit.Core.Tests`.
- **Diagnostic scenes** (all 18): `default, close-cars, offroad, uphill, wreck, arsenal, strike, enemies, boss,
  boss-mirror, missile-warning, weapons-hud, boss-lock, help, traffic, police, roadblock, busted`.

## Review Focus

There are no App tests, so each of these is checked on named snapshots. Each task lists the scenes it must render and
what to look for in them.

1. **A gameplay signal stops reading by shape.** For example: a hostile slick looks like a friendly one; the Gunner's
   burst looks like aiming; the chute looks the same armed or not; the boss shield is hard to see; the GAP word is lost
   in glow.
   Expected: each signal in the Global Constraints list is told apart by shape in its scene.
   *(Task 2: `enemies`, `boss`. Task 3: `arsenal`, `wreck`. Task 4: `roadblock`, `police`, `strike`, `boss-lock`,
   `missile-warning`. Task 6: `boss-mirror`.)*
2. **Small or far things look broken or vanish.**
   - Gaps on a 20 px car read as damage.
   - Far lamps, dashes and wrecks fade to nothing.
   - A thin detail line disappears at 320×200.

   Expected:
   - no gaps on shapes narrower than 30 px;
   - the fade never goes under 0.22;
   - every pen is at least 0.7 px wide.

   *(Task 1: `traffic`, `close-cars`. Task 7: 960×540 set and the stress run's 320×200 frames.)*
3. **Things show through hills or over nearer things.** A restyled wreck, barrier or sign must still be hidden behind
   a crest, and a nearer black fill must still hide a farther line.
   *(Task 1: `uphill`. Task 3: `wreck`. Task 4: `roadblock`.)*
4. **The HUD gets harder to read.** For example: brackets touch or cross text; glow bleeds into numbers; the mirror's
   cars become unreadable.
   Expected: every HUD string sits exactly where it did, with clear space between brackets and text.
   *(Task 6: `weapons-hud`, `help`, `busted`, `boss`, at 1280×720; Task 7 at 960×540.)*
5. **Frames get slower.** A halo, glow and core are three draws per line where there were two.
   Expected: the `--perf` mean stays under 8 ms.
   *(Task 7.)*

## Calls made in this plan

These are my calls, for the owner to check.

1. **Combined is the only look** (design). The switch, the old paths, and the Laser and New Wave pens are deleted, not
   kept as a setting.
2. **Gaps need room.** `LineArt.Polyline` leaves gaps out when the points' bounding box is narrower than 30 px.
3. **Wrecks show their silhouette:**
   - the rear face's sides and top, with no base;
   - the two outer roof ridges;
   - the far roof edge.

   The inner ridges are dropped. The cracks on the rear face stay, as detail.
4. **Smoke has no outlines:** wreck smoke, smoke-screen clouds and mirror smoke stay soft fills. Smoke has no edges to
   present.
5. **Panels get corner brackets, and gauges keep their boxes.**
   - Corner brackets: the weapon panel, radar, help box, notice boxes and boss-bar panel. Each arm is 14 px × the HUD
     scale `K`.
   - Full boxes: the armour, heat, gun-heat, speed, steer and boss-health bars. They are gauges, and a gauge needs its
     ends.
   - The mirror keeps a full frame, because it is an object.
6. **The Gunner's port** becomes a short line across the open bottom of its window:
   - dim while idle;
   - a bright outline while aiming;
   - two parallel lines while firing, so firing differs from aiming by shape.
7. **The windshield cracks and rolling tread stay as built in the trial;** the owner has played them.
8. **The stress test runs once, at the end** (Task 7), at its current length (about 68 minutes, D116), in the
   background. Every renderer changes, so it is worth the one run. The owner's D116 decision for later plans stays open.
9. **The trial branch stays as history.** `line-art` starts from it, so the trial's commits stay in the log.

## File structure

All the files are in `src/NightPursuit.App/`.

| File | Responsibility | Tasks |
|---|---|---|
| `Rendering/LineArt.cs` | the pens, fade, breaks, `Brackets`, `ColorOf` | 1, 6 |
| `Rendering/LineBatch.cs` | batched lines; `Draw(dc, ArtPen)` | 1 |
| `Rendering/VehicleRenderer.cs` | cars, boss, tread | 1, 2 |
| `Rendering/RoadRenderer.cs`, `SkyRenderer.cs` | road, ridges | 1 |
| `Rendering/HazardRenderer.cs` | slicks, wrecks, flames, wreck smoke | 3 |
| `Rendering/ArsenalRenderer.cs` | caltrops, crates, smoke clouds, missiles, strike, lock | 3, 4 |
| `Rendering/RoadsideRenderer.cs`, `GapChevron.cs` | roadblocks, speed traps, GAP | 4 |
| `Rendering/EffectRenderer.cs` | tracers, beams, explosions, muzzle flash | 4 |
| `Rendering/HoodRenderer.cs`, `Mirror.cs` | bonnet, guns, aim mark; mirror | 5 |
| `Rendering/Hud.cs`, `WeaponPanel.cs`, `Radar.cs`, `BossBar.cs`, `HeatMeterView.cs`, `Overlays.cs` | HUD frames | 6 |
| `Rendering/WindshieldRenderer.cs` | cracks (built in the trial) | 1 (tidy only) |
| `Diagnostics/DiagnosticsRunner.cs`, `App.xaml.cs` | drop `--style`/`--sheet`; keep `--damage` | 1 |
| `docs/decisions.md`, Plan 4 | decisions and the Plan 4 updates | 7 |

---

### Task 1: One look: Combined only

**Files:**
- Modify: `Rendering/LineArt.cs`, `LineBatch.cs`, `VehicleRenderer.cs`, `RoadRenderer.cs`, `SkyRenderer.cs`,
  `WindshieldRenderer.cs`, `Diagnostics/DiagnosticsRunner.cs`, `App.xaml.cs`

**Interfaces:**
- Produces (used by every later task):
  - `internal sealed record ArtPen(Pen Halo, Pen Glow, Pen Core)`. The halo and glow are no longer optional.
  - `LineArt.Outline(Color, double fade)`, `LineArt.Detail(Color, double fade)` and `LineArt.Scenery(Color, double fade)`, each returning an `ArtPen`.
  - `LineArt.Fade(double ahead)`.
  - `LineArt.Draw(DrawingContext, Geometry, ArtPen)`.
  - `LineArt.Polyline(IReadOnlyList<Point>, params (double From, double To)[] gaps)`: gaps always apply, except
    under the 30 px rule.
  - `LineArt.Lines(...)`.
  - `LineArt.ColorOf(NeonPen)`, plus a new `LineArt.ColorOf(Brush)` that returns the solid colour at full alpha.
  - `LineBatch.Draw(DrawingContext, ArtPen)`.
  - `LineArt.Dashed(ArtPen pen)`: the same pen with `DashStyles.Dash` on its halo, glow and core (cached). It is for
    hostile outlines, whose dashes are part of their shape (NFR-3).
  - `LineBatch.Draw(DrawingContext, NeonPen)` stays until Task 6 has moved its last caller.
- Removed: `ArtStyle`, `LineArt.Style`, `LineArt.Restyled`, `LineArt.Breaks`, `--style` (diagnostics and App), `--sheet`
  and `WriteSheet`.
- Kept: `--damage` (diagnostics). Its comments lose the word SPIKE.

- [ ] **Step 1: Branch and baseline.**
  - `git checkout -b line-art spike-line-art`.
  - Build Release and render all 18 scenes in the old look, in one launch:
    `NightPursuit.exe --snapshot snapshots/before/s.png --scene <all 18> --style classic`.
  - Expected: exit 0 and 18 files. These are the "before" pictures for every later task.
- [ ] **Step 2: Make Combined the only look.**
  - **`LineArt`:** use the Global Constraints values; the pen cache is keyed by `(Color, level, role)`.
  - **Gaps (Call 2):** add `const double MinGapWidth = 30;`. In `Polyline`, when the points' bounding box is narrower
    than this, draw the line whole.
  - **`VehicleRenderer.Body`:**
    - keep only the restyled branches (tyres, fins, body edge, bumper, tread, open windows, the details skipped when
      restyled);
    - delete the classic branches and the `art` flag.
  - **`RoadRenderer`:** keep the nine `ArtBands` path, delete the three-band classic path, and rename the "SPIKE" comments.
  - **`SkyRenderer`:** `DrawLayeredRidges` becomes the only ridge drawing.
  - **`WindshieldRenderer`:** remove the word SPIKE from the comments; nothing else changes.
  - **Diagnostics and `App.xaml.cs`:** remove `--style`, `--sheet` and `WriteSheet`.
- [ ] **Step 3: Build and test.**
  - `dotnet build src/NightPursuit.App -c Release`: expected 0 warnings, 0 errors.
  - `dotnet test tests/NightPursuit.Core.Tests`: expected 873 passed.
  - `grep -rn "ArtStyle\|Restyled\|SPIKE\|--style" src`: expected no matches.
- [ ] **Step 4: Snapshot** `--snapshot snapshots/after/s.png --scene traffic,close-cars,uphill,enemies,police`.
  Check, against `before/`:
  - every car type still differs by shape;
  - no gaps on cars under 30 px wide;
  - the road and ridges still hide behind the `uphill` crest;
  - the bumper and tread show on near cars.
- [ ] **Step 5: Commit** `refactor: Combined line art is the only look`.

### Task 2: Cars and boss finished

**Files:**
- Modify: `Rendering/VehicleRenderer.cs`

**Interfaces:**
- Consumes: Task 1's pens; `LineArt.ColorOf(Brush)` for `Neon.PortGlow` and `Neon.PortBurst`.

- [ ] **Step 1: The Gunner's port (Call 6).**
  - Replace the port rectangle with a line across the window's open bottom: from `P(-0.3, 0.9)` to `P(0.3, 0.9)`.
  - Idle: `Detail(GunnerColour, fade·0.5)`.
  - Aiming: `Outline(ColorOf(PortGlow), fade)`.
  - Firing: the aiming line plus a second one, `0.08·s` px above it, in `Outline(ColorOf(PortBurst), fade)`.
- [ ] **Step 2: The boss.**
  - **Shield:** draw it as the same open edge as the body (no bottom), with the same gaps, in
    `Outline(ColorOf(shield pen), fade)`. It keeps its pulse between `Neon.ShieldBright` and `Neon.ShieldDim`.
  - **Fins, trim strip and open-armour jag:** keep their fills; stroke them with `Outline` (fins) and `Detail` (trim, jag).
- [ ] **Step 3: The other car details.**
  - The Dropper chute and the police light bar keep their fills and their arming/phase logic; their strokes become
    `Detail` pens of the same colours.
  - Tail lights stay as fills.
  - The armour bar over a damaged enemy, the hit flash colour and the boss tag are unchanged.
  - No `NeonPen` is left in `VehicleRenderer`, except where `PenFor` is read for its colour.
- [ ] **Step 4: Build and test** as in Task 1, Step 3.
- [ ] **Step 5: Snapshot** `--scene enemies,boss,police,close-cars`. Check, against `before/`:
  - the Gunner shows idle, aiming or firing by line count and brightness;
  - the shield pulse is visible and open;
  - the light bars show both colours;
  - the chute reads as armed or not (see `enemies`).
- [ ] **Step 6: Commit** `feat: cars and boss in line art`.

### Task 3: Wrecks, flames, slicks, caltrops, crates and smoke

**Files:**
- Modify: `Rendering/HazardRenderer.cs`, `Rendering/ArsenalRenderer.cs` (caltrops, crates, smoke clouds)

- [ ] **Step 1: Wrecks (Call 3).**
  - Fill the shell and the rear face black, as now.
  - Stroke, with `Outline(Neon.Orange, Fade(nearD))`:
    - the rear face as an open polyline from `NearFace[0]` up and round to `NearFace[^1]`, with no base, and the gaps
      `(0.1, 0.14)` and the right-shoulder gap of the cars;
    - the ridges at indexes `1` and `TopCount` only;
    - the far roof edge `FarFace[1..TopCount]`.
  - The rear-face cracks are drawn with `Detail(Neon.Orange, fade)`.
  - The crest clip (`PushClip` to `clip`) stays.
- [ ] **Step 2: Flames.** Outer tongues `Outline(Neon.Orange, fade)`, inner `Detail(Neon.Amber, fade)`; the flicker
  maths are unchanged.
- [ ] **Step 3: Slicks.**
  - Keep the fill.
  - The rim becomes `Scenery(Neon.Purple, fade)`: only its near half (the lower half-ellipse) when friendly; the whole
    rim when hostile.
  - A hostile slick also keeps its red outline, in `Dashed(Outline(ColorOf(Neon.HostileOutline), fade))`, dashed as now.
- [ ] **Step 4: Caltrops and crates.**
  - Caltrop stars: `Detail(ColorOf(Neon.Caltrop), fade)`; a hostile set keeps its red dashed outline in the same dash
    pattern, drawn with `Dashed(Outline(ColorOf(Neon.HostileOutline), fade))`.
  - Crates: keep the face fills; stroke only the near face's outline without its base, plus the top's far edge, in
    `Outline(ColorOf(Neon.Crate), fade)`.
- [ ] **Step 5: Smoke (Call 4).** Wreck smoke and smoke-screen clouds lose their rim pens; their fills and timing are
  unchanged.
- [ ] **Step 6: Build and test** as in Task 1, Step 3.
- [ ] **Step 7: Snapshot** `--scene wreck,arsenal,uphill`. Check:
  - the wreck reads as a burnt car, with no edge along the road;
  - hostile and friendly slicks and caltrops differ by the red outline's shape;
  - the crates read as boxes;
  - a wreck behind the `uphill` crest stays hidden.
- [ ] **Step 8: Commit** `feat: wrecks and hazards in line art`.

### Task 4: Roadside, effects, missiles and the lock

**Files:**
- Modify: `Rendering/RoadsideRenderer.cs`, `GapChevron.cs`, `EffectRenderer.cs`, `ArsenalRenderer.cs` (missiles,
  strike, lock)

- [ ] **Step 1: Roadblocks.**
  - Barrier faces keep their red and white stripes and black fill.
  - The face and side strokes become open outlines with no base, in `Outline(Neon.Red, fade)`.
  - The barrier lights keep their fills and phase; their strokes become `Detail`.
- [ ] **Step 2: Speed traps.**
  - The post is `Scenery(Neon.Gray, fade)`.
  - The sign keeps its fill, number and `CAUGHT` text; its frame is `Outline(Neon.Amber or Neon.Red, fade)`.
  - The camera box and lens are unchanged in shape.
- [ ] **Step 3: GAP chevrons.** Keep the fill and the word; stroke with `Outline(Neon.Green, fade)`, with no gaps.
- [ ] **Step 4: Effects.**
  - Tracers: `Outline(Neon.Amber, fade)`; hostile tracers keep their own colour.
  - Explosions: `Outline(Neon.Orange, fade)`.
  - The strike column keeps its fill; its core line is `Scenery`.
  - The muzzle flash: `Outline(Neon.Amber, 1)`.
- [ ] **Step 5: Missiles and the lock.**
  - The player's missile body is `Outline`, and its exhaust flame `Detail`.
  - The enemy missile keeps its body and ring, in `Outline(Neon.Red, fade)`.
  - The lock box keeps its shape and label; its lines are `Outline` at fade 1.
- [ ] **Step 6: Build and test** as in Task 1, Step 3.
- [ ] **Step 7: Snapshot** `--scene roadblock,police,strike,boss-lock,missile-warning,arsenal`. Check:
  - the GAP word is readable;
  - the stripes and lights read;
  - `CAUGHT` is readable;
  - the lock box and the enemy-missile ring are clear;
  - tracers read against the road.
- [ ] **Step 8: Commit** `feat: roadside and effects in line art`.

### Task 5: Hood and mirror

**Files:**
- Modify: `Rendering/HoodRenderer.cs`, `Rendering/Mirror.cs`

- [ ] **Step 1: Hood.**
  - The bonnet line is drawn as an open polyline along its top edge only, in `Outline(Neon.Green, 1)`, with a gap
    `(0.56, 0.6)`. The black fill stays.
  - The gun barrels: `Outline(Neon.Green, 1)`.
  - The aim mark: `Outline(Neon.Green, 1)`, the same shape.
- [ ] **Step 2: Mirror.**
  - The frame stays a full rectangle (Call 5), in `Detail(Neon.Cyan, 1)`.
  - The mirror road is `Scenery(Neon.Cyan, LineArt.Fade(-d))`, so it fades with distance behind you.
  - Mirror cars: a black fill and an open outline (left, top, right; no bottom) in `Detail(car colour, 1)`.
  - Light bars, boss trim, tail lights, smoke, hostile dots and enemy missiles keep their shapes; their strokes become
    `Detail`.
- [ ] **Step 3: Build and test** as in Task 1, Step 3.
- [ ] **Step 4: Snapshot** `--scene default,boss-mirror,police,busted`. Check:
  - the aim mark is clear;
  - the mirror cars are readable, and police cars show both light colours;
  - the boss in the mirror still shows its trim.
- [ ] **Step 5: Commit** `feat: hood and mirror in line art`.

### Task 6: HUD frames

**Files:**
- Modify: `Rendering/LineArt.cs`, `Hud.cs`, `WeaponPanel.cs`, `Radar.cs`, `BossBar.cs`, `HeatMeterView.cs`,
  `Overlays.cs`, `LineBatch.cs`

**Interfaces:**
- Produces: `LineArt.Brackets(DrawingContext dc, Rect r, double arm, ArtPen pen)`. It draws two lines at each corner
  of `r`, each `arm` long, along the rectangle's edges.

- [ ] **Step 1: Add `Brackets`.**
- [ ] **Step 2: Panels (Call 5).** These keep their fill and get `Brackets(rect, 14·K, Detail(Neon.Cyan, 1))` in place
  of their full frame:
  - the weapon panel (`WeaponPanel.cs:32`);
  - the radar box (`Radar.cs:26`, in `Detail(ColorOf(Neon.RadarLine), 1)`);
  - the help box and notice boxes (`Overlays.cs:96, 110, 216`);
  - the boss-bar panel.
- [ ] **Step 3: Gauges.** These keep full boxes, now drawn with `Detail(Neon.Cyan, 1)` in place of `Neon.Hud.Core`:
  - the armour, speed, steer and gun-heat bars and the heat meter outline (`Hud.cs`, `WeaponPanel.cs:41, 77`,
    `HeatMeterView.cs`);
  - the boss health bar (`BossBar.cs:61`).
- [ ] **Step 4: Tidy the old pens.**
  - If nothing calls `LineBatch.Draw(dc, NeonPen)` any more, remove it.
  - Delete `NeonPen` fields left unused in `Neon.cs`.
  - Radar blips and other non-frame uses may keep their `NeonPen`s; list any that remain in the report.
- [ ] **Step 5: Build and test** as in Task 1, Step 3.
- [ ] **Step 6: Snapshot** `--scene weapons-hud,help,busted,boss,default`. Compare each string's position with `before/`.
  - Expected: every string is in the same place;
  - no bracket touches text;
  - the gauges still show their ends.
- [ ] **Step 7: Commit** `feat: HUD frames in line art`.

### Task 7: Full check, decisions and the Plan 4 updates

**Files:**
- Modify: `docs/decisions.md`, `docs/superpowers/plans/2026-11-20-plan-4-missions-screens-and-scores.md`

- [ ] **Step 1: All scenes at two sizes.**
  - `--snapshot snapshots/after/s.png --scene <all 18>` (1280×720 by default).
  - Then the same at `--size 960x540` into `snapshots/after540/`.
  - Make one before/after sheet per scene with the throwaway script.
  - Expected: nothing from `before/` is missing in `after/`, except the full edges the design removes.
- [ ] **Step 2: `NightPursuit.exe --perf snapshots/perf.txt`.** Expected: mean under 8 ms. Record the mean, the 95th
  percentile and the max.
- [ ] **Step 3: Stress (Call 8):** `NightPursuit.exe --stress snapshots/stress.txt`, run in the background. Expected:
  exit 0, 0 failures. Record the frames and minutes.
- [ ] **Step 4: Decisions.**
  - Add D117 onward to `docs/decisions.md`: one per Call above, plus "the owner dropped the wreck breakdown effects",
    plus any rulings made during the build, in the log's style (what, why, *Cost to change*).
  - Under "Open items", add anything found.
- [ ] **Step 5: Plan 4 updates.**
  - Its branch is created from `line-art`, not `plan-3-traffic-and-police`.
  - Its decisions start at the log's next free number, not D117.
  - Task 8's boss looks and the briefing's boss drawing use the `LineArt` pens (outline, gaps, no base line).
  - Its test count is 873 plus its own.
- [ ] **Step 6: Checks only you can do (line art)**, added to `docs/decisions.md`:
  1. Play mission 1 and the patrol ground. Judge the look in motion: the glow, the gaps, the fade, the tread, the cracks.
  2. Check that every enemy type is told apart by shape at a distance.
  3. Check the HUD is as easy to read as before.
  4. Say whether the panel brackets look right, or whether you want full boxes back.
- [ ] **Step 7: Commit** `docs: decisions for the line-art restyle`.
