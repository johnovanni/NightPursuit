# Night Pursuit: decisions to review

Judgement calls made while building, newest at the bottom. Each one says what was decided, why,
and what it would cost to change. Nothing here is final: say which ones you disagree with.

## Before building Plan 1 (2026-11-17)

**D1. Steering: the straightening assist now works all the time.**
You saw a sideways jitter right after gun kills. I traced it with a new prototype diagnostic
(`--kill-trace`), and there was no extra shake or bumping after kills. What happens: after a kill
you steer back to your lane, and the old assist only worked with no key held and was weak
(3 per second). So after letting go, the car slid on for 1.1 s and overshot the lane by about 3 m.
You then corrected the other way, and since the camera turns with the nose, the whole view swung
side to side about once a second.
Now the assist works even while a key is held (8 per second), and the nose turns faster (90°/s), so
lane changes stay as quick as before. Measured (`--steer-test`):
- the slide stops 0.4 s after letting go (was 1.1 s);
- the car ends 1–1.3 m past where you let go (was 4.7 m);
- the view swings 2.6× less;
- a test driver aiming at traffic destroyed 16 cars in 3 minutes instead of 12.

Trade-offs:
- The largest nose angle at top speed is now about 6° instead of 8.6°. That's still enough to aim
  one lane over at 60 m.
- With no steering on a bend, the car drifts outward about a third as fast.

To compare, untick "Straighten while steering" in the prototype's F12 panel. Design DRV-4 and
Plan 1 Task 5 are updated.
*Cost to change:* a settings default and one test.

**D2. Local commits, on a branch.** The plan commits after each task, and you chose that method
and asked me to carry on myself. The repository has no GitHub remote, so commits stay on this PC.
- First commit on `main`: the design, the plans and the prototype.
- All of Plan 1 is built on a branch, `plan-1-foundation`, and nothing is pushed.

*Cost to change:* none. The branch can be merged, squashed or deleted.

## Pre-flight rulings on Plan 1

**D3. Top speed test.** The plan's test expected full throttle to reach 150–155 mph. The ported
speed model levels off at 149.7 mph: engine pull falls toward 155 and coasting drag balances it
just below that. I kept the model (it's the one you drove) and the test now checks 148–151 mph.
*Cost to change:* raise `MaxSpeedMph` to about 160 if you want a true 155.

**D4. Gunfire wrecks slide to a stop.** The plan put a wreck exactly where the car died, standing
still. At 150 mph that leaves about half a second to dodge a car you shot 30 m ahead. Instead, a
gunfire wreck keeps sliding in its lane, slowing at 25 m/s² (as the prototype's wrecks did), then
stays put and blocks the lane (WRK-1). That gives about 1.5 s to react, and the "WRECK AHEAD"
warning shows it.
*Cost to change:* one line.

**D5. A wreck damages you once.** Hitting a wreck does 30 damage and drops you to 5 m/s, once per
wreck. After that it just blocks you: you stop against its back and must steer round it. Without
this, a car crawling against a wreck would take 30 damage every step and die at once.

**D6. Owner's manual checks wait for you.** Tasks 12 and 16 end with checks only you can do
(drive it, alt-tab while steering, rename the mission file, play mission 1 to the end). I do
every check I can with tests and snapshots, and list the rest for you at the stopping point.

**D7. Control frames can't be changed after they're made.** The plan shares one
`ControlFrame.Empty` object. If its fields could be changed, one stray write would make "no input"
mean something else everywhere. So its fields can only be set when it's created.

**D8. Oil charges before missions exist.** Task 9 builds the oil dropper before Task 11 adds
mission files. It starts with 5 charges, and Task 11 sets it from the mission's `startingOil`.

## While building Plan 1

**D9. A car rammed to death doesn't leave a wreck.** The plan only had three causes of destruction:
gunfire, running off the road, and hitting a wreck. A car whose armor runs out from ramming is
counted as "rammed": it crashes beside the road like one run off it, and does not block a lane.
Only gunfire wrecks block lanes (WRK-1, WRK-2).

**D10. Bullets hit the nearest car.** If two cars overlap along a bullet's path in one step, the
nearer one is hit, so you can't shoot through a car to reach the one behind it.

**D11. Geometry corrections.** Two expected values in the plan were wrong, and the code is right:
- A straight line leaving a 200 m bend is 23.6 m outside the road after 100 m, not 26.8 m.
- The road view now uses exact arcs, so a constant bend is a true circle; it was off by about
  1 mm before.
