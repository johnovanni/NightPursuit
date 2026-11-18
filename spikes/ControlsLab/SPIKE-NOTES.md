# Controls Lab: throwaway prototype

**Purpose:** to find out which controls feel right for Night Pursuit before writing the real
requirements and architecture. This code is a sketch. It has no tests and the real game will not
build on it. What we learn here goes into the design.

## Run it

```
dotnet run -c Release --project C:\repos\NightPursuit\spikes\ControlsLab
```

Snapshot without opening a window (for checking the look):
`ControlsLab.exe --snapshot out.png [--seconds 12] [--scheme 0-3] [--help-overlay] [--close-cars]`

Crash check, rendering a frame every 10 m round the whole track (about 2.5 minutes):
`ControlsLab.exe --stress report.txt` (exit code 1 if any frame fails)

## Fixed

- 2026-11-17: crash "Width and Height must be non-negative" about 15 s into a drive. A sharp bend
  swung the road sideways past the camera, so its far end projected far above the top of the screen.
  The "hidden behind a hill" line went negative, and drawing a car clipped to it threw. Now only road
  that is still heading away can hide things, the line never goes above the screen top, and cars are
  skipped when fully hidden. The stress test reproduced the crash on 11 frames before the fix and
  passes on all 2000 after it.
- 2026-11-17: steering "jitters" lasting a few seconds. Cause: at speed, a held key turned the nose
  13–18° off the road, so the car slid sideways at 14–17 m/s, overshot the road edge, and the
  off-road rumble shook the view, with only a small "OFF ROAD" word as the reason. Fix: the nose limit
  shrinks with speed so sideways speed stays under 10 m/s (F12 "Max sideways speed"), plus a large
  "OFF ROAD" banner with an arrow. Measured with `--trace trace.csv`: a lane-keeping driver went from
  7.4 s off the road to 0, with the nose now at most 8.7°.
- 2026-11-17: sideways "jitter" right after a gun kill. Cause: after a kill the player steers back to
  their lane, and the straightening assist only worked with no key held and was weak (3/s), so after
  letting go the car slid on for 1.1 s and overshot the lane by about 3 m. The player corrected the
  other way, and since the camera turns with the nose, the whole view swung side to side about once
  a second. Fix: the assist now works all the time (8/s, F12 "Straighten while steering") and the nose
  turns faster (90°/s) so lane changes stay quick. Measured with `--steer-test`: the slide stops 0.4 s
  after letting go (was 1.1 s), the car ends 1–1.3 m past where you let go (was 4.7 m), the view swings
  2.6× less, and the test driver destroys 16 cars in 3 minutes instead of 12. Also `--kill-trace`
  (shake and slide around each kill) and `--kill-film sheet.png` (16 frames after a kill).

## The four throttle schemes (switch any time: Tab, or F2 to F5)

| # | Scheme | Up / W | Down / S | Extra |
|---|--------|--------|----------|-------|
| 1 | Ramp pedal | throttle builds while held, falls away when released | brake | C = cruise control |
| 2 | Speed lever | raises the target speed | lowers the target | 1-9, 0 jump to 10%-100% |
| 3 | Gearbox | gas (builds like the ramp) | brake | Q/E or PgDn/PgUp shift through 4 gears |
| 4 | Scroll-wheel lever | full boost while held, then back to target | brake, then resume target | wheel = 5 mph per notch |

A gamepad (Xbox-style) works in every scheme: the triggers are a real analog gas and brake, and the
left stick steers in proportion to how far you push it.

## Other keys

Steer: Left/Right or A/D · Guns: Space (hold) · Oil slick: X or Ctrl · Reset: R · Pause: P ·
Help: F1 · Tuning panel: F12 or T

Steering hard into the side of a car knocks it into a spin. A spinning car that leaves the road crashes.

## Questions to answer while playing

1. **Throttle:** which scheme felt most natural in a fight? Which felt best just driving fast?
2. **Throttle feel:** in the ramp pedal, is the build-up too slow or too fast? (F12: "Throttle build-up")
3. **Feathering:** can you hold a middle speed by tapping, or do you want cruise / the lever for that?
4. **Steering:** too twitchy or too sluggish at top speed? Try "Steering build-up", "self-centre",
   and the "lighter steering at high speed" tick box.
5. **Curves:** is the outward pull on bends fun, annoying, or too weak? ("Curve pull")
6. **Weapon keys:** Space and X work while steering with the arrow keys. Are they comfortable,
   or would you rather fire with the other hand on WASD?
7. **Behind you:** with the mirror, radar and the flashing "PURSUER BEHIND" warning, can you time
   an oil slick? Is anything missing on screen?
8. **Ramming:** does knocking a car into a spin feel like a weapon or an accident?
9. **Gamepad:** if you have one, does it beat the keyboard by a lot?

## Calls I made (all open to change)

- **Working title:** "Night Pursuit". The game will be original: no names, cars, artwork or music
  from Spy Hunter or Speed Freak.
- **View:** 70° field of view, driver's eye 1.15 m above the road, hood and twin guns drawn at
  the bottom of the screen.
- **Road:** three lanes, 15 m wide, on a 20 km loop with bends (radius 180-700 m) and gentle hills.
  The road ahead and behind is computed from its real geometry, so tight bends wrap round correctly.
- **Speed:** default top speed 155 mph. With throttle, top speed comes from a fall-off in engine pull,
  not a hard cap, so the car pulls hard at low speed and creeps up near the top.
- **Tracers follow the road:** bullets stay in your lane even on bends. This is an arcade
  simplification.
- **Everything important is shown on screen.** There is no sound at all. Hits flash red at the
  screen edge, the pursuer is shown in the mirror, the radar and a flashing warning with a side
  arrow, and every event appears as a text message.
- **Three enemy behaviours to test against:** Runner (magenta, drives and changes lanes),
  Blocker (amber truck, moves into your lane and brake-checks you), and Pursuer (red, comes from
  behind and tries to sideswipe you).

## Playtest feedback (2026-11-17)

- The controls work fine. It "seems like it will be fun."
- Bullets follow the curves of the road. Proposal: fire them straight along the car's heading
  instead, so aiming on bends takes skill.
- New ideas for the game:
  - civilian cars on the road;
  - police that need to be avoided;
  - a car destroyed by machine-gun fire leaves its wreck blocking the road, unlike one run off the road.

## Turn-to-aim steering (added 2026-11-17, the owner's choice)

Steering now turns the car's nose, up to 20° from the road's direction, and the car moves the way it
points. The camera turns with the car, so the screen centre is the gun line. Tracers fly straight, so
on a bend they leave the road unless you turn in. With no steering input, a straightening assist
turns the nose back toward the road. The HUD shows "NOSE 12° RIGHT" and so on, and the radar
arrow turns with the car.

F12 sliders for it: nose turn rate (default 90°/s, halved at top speed if "lighter steering" is on),
max nose angle (20°), and straightening assist (8 per second, always on unless "Straighten while
steering" is unticked; on a bend the car drifts outward at about speed² × curvature ÷ 8 m/s). The old "steering strength" and "curve pull" sliders are gone;
the assist replaces curve pull.

Questions: does aiming feel fair on bends? Is the nose too twitchy or too slow at 150 mph?

## Known shortcuts (fine for a spike)

Enemies pass through each other · no smoke, caltrops, missiles or satellite strike yet · enemies
don't shoot · no missions or boss · one fixed track · the camera doesn't lean into turns.
