# Night Pursuit — Line-Art Restyle (short design)

**Date:** 2026-11-20 · **Size:** a small project, done before Plan 4 is built · **Trial:** branch `spike-line-art` (5e943cb, 17126c2)

## Intent

The owner wants the game redrawn in a minimalist 1980s line-art style, to give the pictures emphasis and depth. Today
many objects show every edge; the owner wants to see only the **presenting edges**, the ones that face the viewer.

## Approaches (owner picked Combined, with a short broken bumper line)

| Style | What it is | Trade-off |
|---|---|---|
| Laser | Soft bloom on every line, no breaks | Strong glow and depth; busiest look |
| New Wave | Crisp lines, no glow, breaks where light catches, heavy outlines | Most elegant; far road and lamps get faint |
| **Combined** | New Wave's breaks and weights with a softer glow | Depth of Laser, economy of New Wave |

The trial showed all four side by side (snapshots `line-art-*.png`). A follow-up trial added a short bumper line,
broken in the middle and stopping short of the sides, so near cars stand on the road instead of reading as arches.

## Goals

1. **Presenting edges only.** Black fills hide what is behind. No line where an object meets the road (body bottoms,
   tyres show only their outer side, with no line along the road, which read as a tank's tread; windows open at the bottom). Each car (not the boss) has a bumper line
   just above the body's bottom edge (0.30 m; the body ends at 0.224 m), running to 90% of the body's half-width each
   side, with a small break in the middle. (The owner moved it down from the first trial's 0.45 m, which left near cars
   looking like arches.)
2. **Line-weight hierarchy.** Outline heavy, inner detail light, scenery between (the trial's `Outline`, `Detail`,
   `Scenery` pens).
3. **Breaks where light catches.** Light from the upper right: a gap past the right shoulder of an outline and a
   small one low on the left. Applied to outlines only, not to detail lines.
4. **Depth fade.** Lines dim and thin with distance (`LineArt.Fade`), the road in nine bands, two mountain ridges with a
   dim far range behind a filled near range.
5. **Everything in the world view and the mirror** gets the style: cars and boss (fins, shield), wrecks and flames, oil,
   spike strips and smoke, roadblocks and speed traps, the GAP chevron, posts and lamps, tracers, explosions, the
   satellite strike, the bonnet and gun barrels, and the mirror's road and cars.
6. **HUD:** panels keep their text exactly as readable as now; their frames move to the same pens (corner brackets
   instead of full boxes, where a full box adds nothing). Text is never broken or faded.

7. **Rolling tread.** On cars close enough for the tyre strip under the body to be at least 7 px tall, three dim bars
   slide down that strip, as the back of a turning wheel does. Their phase comes from the distance the car has driven
   (one bar per 3.5 m), so they stop when it stops and never seem to roll backwards (under half a bar per frame at
   100 m/s). No flashing.
8. **Windshield cracks (owner's request).** Cracks appear in the driver's windshield and spread as the car takes damage.
   They follow the car's worst damage so far (`PlayerCar.WorstDamage` = 100 minus its lowest armor), so they do not go
   away when armor is regained. My calls: a new car (respawn or new mission) starts with a clean windshield and a new
   pattern; the first chip shows when armor first drops below 96; seven impacts appear by 84 damage, each growing over
   30 damage from a chip with jagged radial cracks to branches and then a broken spider-web ring; impacts are placed
   clear of the aim mark; drawn thin and pale over the hood and under the HUD and mirror, so all readouts stay on top.
   `WorstDamage` is presentation only and is not in StateHash.

9. **The car breaks down (owner's request).** From the moment the player's car is wrecked (a wall clock in the view,
   so it also plays on the frozen Game Over screen), over the three-second respawn:
   - the windshield jumps to a full web of cracks with a faint milky haze (within 0.35 s);
   - the bonnet line buckles at four points and the right gun barrel bends down (0.3 s); the aim mark goes;
   - grey smoke rolls up from under the bonnet, and small flames sway at the two worst buckles;
   - the nose dips (the world rises 3.5% of the screen height) and the view leans 4 degrees (over 1.4 s);
   - the dashboard (HUD, weapon panel, radar, boss bar) dips twice and fades to 12% brightness; the mirror stays;
   - **dizzy double image:** a 42% copy of everything except the messages circles the real view on a tight circle
     (1.2% of the screen width, one turn every 1.4 s), fading in from 0.3 s.
   Warnings, messages and the WRECKED banner with its countdown and spare-car count are drawn once, on top, and stay
   sharp. The new car restores everything. Nothing flashes quickly: two dashboard dips in about 0.7 s, and slow flames.

## Non-goals

- No gameplay change. Core is untouched; StateHash and all 869 tests stay as they are.
- No new colours; each type keeps its colour.
- Plan 4's screens are not built here. Plan 4's boss portrait and briefing pictures will use these pens (a note is added
  to Plan 4, not a new task here).

## Anti-goals (failure even if it works)

- A car type that can no longer be told apart by **shape** (NFR-3: never by colour alone).
- HUD text, numbers or warnings that are harder to read. Nothing important may rely on faint lines or on sound.
- A slower frame: the `--perf` budget from earlier plans must still hold.

## Decisions (my calls, for the owner to check)

- **Combined becomes the only look.** The trial's switch, Classic paths, Laser and New Wave code are removed, not kept
  as a setting. Reason: one look to test and maintain; nobody asked for a choice in Settings. Cost if wrong: a small
  task to add a setting back.
- **Gunner's rear window** loses its gun-port fill showing through the open bottom; the port is drawn as a short line.
- **Gaps are dropped below a minimum on-screen size** (about 30 px wide), where they only look like flicker.
- **Wreck faces:** only the faces turned toward the camera are stroked; hidden faces are filled black only.

## Left to the builder

Exact gap positions per shape, glow widths within the trial's ranges, and which HUD boxes become brackets. Each is
checked by eye on snapshots of every scene (`--snapshot --scene ...`), with sheets sent to the owner at the end.

## How it is checked

- Core tests unchanged and green; StateHash unchanged.
- Snapshots of every diagnostic scene at 1280x720 and 960x540, compared against the current look.
- `--perf` within budget.
