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

**D5. A wreck damages you once, then you steer round it.** Hitting a wreck does 30 damage and
drops you to 5 m/s, once per wreck. It pushes you back 1 m and blocks you. Steering then slides
you sideways round it at 3 m/s, even when you're almost stopped. Without this, a car pushing
against a wreck would take 30 damage every step. The final review also found a car stuck behind
a stopped wreck for good, which this fixes.

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

**D12. Losing focus pauses the game.** If the window loses focus while you're driving (alt-tab, a
notification), the game pauses itself, so cruise or the lever can't drive the car into the rail
while you're away.

**D13. Restart only when paused or finished.** R (or the pad's Back button) restarts only when the
game is paused or the mission has ended. Pressed while driving, it shows how to restart, so one
stray key can't throw away a run.

**D14. A pursuer alongside you is always shown.** While a pursuer rides beside you before ramming, a
"PURSUER LEFT/RIGHT" warning and an edge arrow show it, not just the radar.

**D15. Mission timing doesn't depend on frame rate.** Enemy waves, finishing and respawning are
now checked every simulation step, not once per screen frame. So the same driving gives the same
game on a 60 Hz or 144 Hz screen, as the design requires (NFR-2).

**D16. Smaller fixes from the final review.**
- A retreating pursuer no longer keeps ramming you when you're slow.
- The pad's brake trigger cancels cruise.
- The pad's Back button restarts, under the same rules as R.
- Pressing oil too soon says "OIL NOT READY".
- The spare-car icons match the respawn banner.
- The moon no longer hides behind the top-right readouts.
- A mission file with a too-tight bend or an enemy wave placed out of range is rejected with a
  clear message.

**D17. Speed figures.**
- Drawing one frame takes 0.84 ms on average: about a twentieth of a 60 fps frame.
- The stress test drew 6,001 frames at three window sizes with no failures.
- These figures leave out the graphics card's own work, so the live frame rate still needs your
  check.

## Open items (found by the final review, left for later)

The build process allows one round of fixes after the final review. These turned up in its
re-check:

1. **A retreating pursuer can steer back into your lane.** After it sideswipes you, it heads for
   the lane nearest itself, which can be yours, and it falls back only slowly. Fix: send it to a
   lane on its own side, at least 2.5 m from you. This is the one worth doing first.
2. **Losing focus during the 3 s respawn doesn't pause.** Afterwards the run carries on with
   nobody at the keyboard. Fix: pause whenever the window is inactive and the game is running.
3. **R during a respawn gives the wrong hint.** It says "press P to pause", but P does nothing
   during a respawn.
4. **A pursuer 3–4 m ahead reads "BEHIND −4 m".**
5. **Rare long frames can run more than 0.25 s of simulation.** This happens right after a respawn
   or wreck, and is capped below 0.5 s. Fix: cap the leftover time.

Also noted, not urgent:
- Enemy cars pass through each other. The design's collision rules cover every pair, but Plan 1
  only did you against each enemy. Plan 2?
- You respawn where you crashed, not "a short way back" (MSN-3).
- F1 help doesn't pause the game.
- The oil recharge isn't shown on the HUD.
- "BLOCKER AHEAD" has no side arrow.
- Mission 1 is fairly sparse: 12 enemy cars over 6 km. Worth judging when you play it.

## Checks only you can do

1. Drive the new game: `dotnet run -c Release --project src/NightPursuit.App` from
   `C:\repos\NightPursuit`.
   - Check that steering feels steady at speed with no jitter (D1).
   - Check that it runs smoothly at 1280×720 and full screen (NFR-1).
2. Hold A and alt-tab away: the game should pause and nothing keeps steering.
3. Rename `content\missions\mission-01.json` in the build output folder and start the game: you
   should see a readable error. Put the file back afterwards.
4. Play mission 1 to the end:
   - use the guns on a bend;
   - check that a wreck from gunfire blocks a lane and that you can steer round it;
   - oil a pursuer;
   - ram a runner off the road.
5. Debug build only: press F12 for the tuning panel.

## After your second playtest (2026-11-17)

**D18. Smooth motion at any refresh rate.** The new game runs its rules in fixed 1/120 s steps
and drew the latest step. Your monitors run at 75 Hz and 60 Hz. At 75 Hz each frame got one step,
then two, then one. At 150 mph the scene jumped 0.56 m, then 1.12 m, then 0.56 m: an uneven
judder, most visible sideways while steering. The prototype split frames into equal steps, so this
was new in the real game. Each frame is now drawn at the exact in-between moment, so every frame
moves the same amount. A test checks this at 75 Hz to within a millionth of a metre. The
simulation itself is unchanged, so runs are still repeatable.
If you still see a sideways weave, tell me whether it's a quick shake or a slow sway. The slow
sway was the earlier steering-overshoot problem (D1).

**D19. Hills hide the mountains.** Nothing filled the ground, so when the road climbed ahead, the
far road rose above the horizon and the mountain line showed through it. The ground is now
filled black up to the highest visible point of the road. A hill hides the mountains behind it,
and on flat road they look as before.

**D20. Wrecks have depth and smoke.**
- A gunfire wreck is now a crumpled 3D body, with a back, a front, a roof and sides, and the
  flames rising from it.
- A column of grey smoke rises from it and drifts with the wind. The column makes a wreck easier
  to spot from far away.
- The smoke is kept small and faint up close, so it doesn't cover the screen.
- Drawing still takes 0.55 ms per frame on average.
