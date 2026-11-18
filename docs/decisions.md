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

## Open items (found by the final review; items 1–5 fixed later, see D21)

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

**D21. The five open items are fixed.**
- A retreating pursuer stays on its own side of you, at least 2.5 m away. At the road edge, if
  there's no room, it holds its line until it's 20 m back.
- It's always slower than you while retreating, so it really drops back instead of creeping up.
- The game pauses whenever the window isn't active, including just after a respawn.
- R during a respawn says "RESTART AFTER THE RESPAWN".
- A pursuer just ahead reads "PURSUER LEFT/RIGHT", not a negative distance.
- One frame can never run more than 0.25 s of game time.

**D22. Where the top of the screen puts things when the boss bar is up.**
- The boss bar sits under the mirror; the warnings start just under it, and the messages start below the
  last of the four possible warning lines, so nothing overlaps.
- The announcement banner sits at half the screen height on a dark box, below the messages and the aim mark.
  It fades in over 0.2 s and out over 0.4 s.
- Warning lines are 22 apart (the brief said 24): four lines at 24 would push the messages down onto the aim mark at 720p.
- Messages stop above the aim mark: with the boss bar and a 1280x720 window that is three rows (the oldest drops out);
  the banner starts below whichever is lower, half the height or the last message row.
- The left edge arrow sits below the weapon panel.
- The top-left help line now reads "R restart when paused" so the boss bar doesn't cut it off.

## Plan 2 calls (before building)

The design left these open, so I picked a default for each before building Plan 2 (weapons, new enemies and
bosses), and the game follows them. I checked every entry below against the finished code; where the build ended
up a little different from the plan, the entry says what the game really does.

**D23. Weapon keys.** Z or Shift picks the next rear weapon, X or Ctrl drops it, F fires a missile and V calls
the satellite strike. On the gamepad: X picks the next rear weapon, B drops it, RB fires a missile and LB calls the
strike. Your left hand does all the weapons, and the arrow keys stay under your right. F1 shows the full list.
*Cost to change:* a few lines in the key table and the help text.

**D24. Only the oil recharges.** Oil comes back one charge every 8 s, as before. Smoke (up to 3), caltrops (3),
missiles (2) and the strike (1) come only from what the mission gives you at the start and from supply crates. The
design gives recharging to oil alone, and this gives the crates a purpose.
*Cost to change:* one number per weapon.

**D25. A short wait between uses.** Oil 0.6 s (as before), smoke 1 s, caltrops 0.8 s, missile 1 s. A bouncing key
can't empty a weapon in one go. Pressing too soon says so, for example "SMOKE NOT READY".
*Cost to change:* one number each.

**D26. The smoke cloud is big, and drifts along with you.** It is 30 m long and 12 m wide, and it's dropped 6 m
behind you. It starts moving forward at half your speed, slows to a stop, and lasts 5 s. A small puff that stood
still would be crossed by a pursuer in a tenth of a second.
*Cost to change:* four numbers.

**D27. What smoke does to a car.** While a car is inside the cloud, and for 2 s after, it can't see you:
- it drives at 60% of its usual speed (never below about 34 mph);
- every 0.8 s it heads for a random spot up to 4 m to either side, never more than 6 m from the middle of the road;
- it can't aim, fire or drop anything.

"RUNNER LOST YOU IN THE SMOKE" (or whichever kind) shows when it happens. A Gunner blinded while it was aiming
stops showing "GUNNER AIMING" at once, and its gun port goes dark, because it can't fire. The plan said "anywhere within 6 m"; a
pre-flight check settled on "up to 4 m from where it is", so a blinded car drifts rather than jumping across the road.
*Cost to change:* a few numbers.

**D28. Every pair of cars can now collide.** This was an open item from Plan 1: enemy cars no longer pass through
each other.
- A car that runs into the back of another is held behind it and slowed to just under its speed. Both lose half a
  point of armor.
- Cars that touch side by side are pushed apart. If they come together faster than 4 m/s, both spin.

So that this doesn't happen all the time in ordinary traffic, every enemy car that can see keeps clear of a slower car
ahead in its lane. It moves to a clear lane beside it, or else matches its speed, after its usual reaction delay
(D53 says how far ahead it looks). A car blinded by smoke can't do this, which is how cars crash into each other
inside smoke.
*Cost to change:* the keep-clear rule is one part of the code; the collision numbers are one line each.

**D29. Caltrops.** You drop them 5 m behind you, in the middle of the lane you're in. A set covers that one lane for
3 m of road and lasts 30 s. A car that drives over it gets a puncture: it slows by 5 m/s every second, wobbles up to
0.8 m from side to side, limps to the nearer edge of the road and crashes beside it. Like a car run off the road,
that leaves no wreck in a lane (WRK-2), and you still get its points.
*Cost to change:* a few numbers.

**D30. A puncture doesn't stop anything else happening to a car.** A punctured car can still be spun by oil, shot or
rammed, like any other.
*Cost to change:* none to keep it; changing it would touch every rule that checks what state a car is in.

**D31. Enemy oil and caltrops affect you; your own never do.**
- Enemy oil: for 1 s your steering has no grip, you're shoved sideways a little and you lose a tenth of your speed.
  "OIL - NO GRIP" shows.
- Enemy caltrops: they cost 10 armor, then for 5 s your car is punctured. Above 100 mph the throttle is cut and a
  gentle automatic brake slows you (150 down to 100 mph takes about 5 s), and the steering wobbles slightly. Your own
  brake still works; below 100 mph it is just a speed limit. "PUNCTURED" shows.

You can see both on the road before you reach them, with "OIL AHEAD" or "CALTROPS AHEAD", and neither can wreck you by
itself (D57).
*Cost to change:* a few numbers.

**D32. Missiles.** A missile leaves about 250 mph faster than you and flies 700 m. It locks onto the nearest enemy 15
to 500 m ahead within 25° of your nose (the view is 70° wide, less a margin). A green LOCK box shows which car, and the
weapon panel says, for example, "LOCK: RUNNER 140 m". It steers after that car and destroys any ordinary enemy,
leaving a burning wreck; the boss loses a fifth of its health instead. With nothing to lock onto it flies straight
and says "MISSILE AWAY - NO LOCK". One that flies its full range without hitting anything says "MISSILE LOST".
*Cost to change:* a few numbers.

**D33. Satellite strike.** If there is no enemy in range (150 m behind you to 400 m ahead) it refuses with "NO
TARGETS IN STRIKE RANGE" and you keep it, because strikes are rare. Once called, the 1.5 s targeting sweep always
lands, even if you're wrecked during it. Every enemy in range is destroyed; the boss loses 35% of its health instead
(D41's limits still apply).
*Cost to change:* one line for the refusal; a few numbers.

**D34. Supply crates.** Glowing 1.6 m boxes with a label such as "MISSILE +1", placed by the mission file. Each one
appears on the road 800 m before you reach it. Only you can collect them; enemy cars and bullets pass through them.
Driving into a crate for a weapon that's already full still uses it up, and says "SUPPLY: SMOKE ALREADY FULL". A sixth
kind, Armor, repairs 25 for each one in the crate.
*Cost to change:* a few numbers.

**D35. What you start a mission with.** Each mission file says how much of each weapon you start with (oil, smoke,
caltrops, missiles, strikes). A misspelled or unknown weapon name is rejected with a clear message. The old
"startingOil" setting still works on its own; using both is an error. Mission 1 gives you 5 oil, 1 smoke, 1 caltrops,
1 missile and no strike.
*Cost to change:* none.

**D36. The Gunner.** An orange-red fastback with a square gun port in its rear window. It gets ahead of you, holds
about 45 m in front and follows your lane. Before it fires, the gun port glows and "GUNNER AIMING - LEAVE ITS LANE"
shows for 0.6 s, and it stops following you, so changing lanes dodges the burst. Then it fires 6 shots backward
(the port turns white and the words stay up while it fires) and waits 2.5 s before it can aim again. Each hit costs
you 2 armor. Its bullets reach 120 m and never hit other cars. It only aims when it's 25 to 90 m ahead and in your
lane. A boss in a Gunner phase does all of this too, in its own words (D67).
*Cost to change:* a few numbers.

**D37. The Dropper.** A teal van with a tall hatch and a chute. It holds ahead of you in your lane and drops oil and
caltrops in turn, about one every 4 s: after each drop it waits 3.5 s, then its chute glows for 0.5 s before the next.
It only drops where you will have at least 1.3 s to react, measured from your front bumper to the near edge of the
hazard, and never closer than 60 m (at 150 mph that is about 87 m). "DROPPER: OIL AHEAD" or "DROPPER: CALTROPS AHEAD"
shows. A boss in a Dropper phase does the same, with two chutes and its own words (D67).
*Cost to change:* a few numbers.

**D38. The Armored car.** The design only said it is tough. It's a steel-blue wedge with armour plates.
- While it's ahead of you, it lumbers along at its cruising speed.
- Once you pass it, it chases you, rams you from behind, backs off for 2 s, and repeats. It chases at up to about
  143 mph, so at full speed you can just pull away from it.
- Your guns point forward, so the answers are a hazard, a ram, or braking to let it pass and then a missile.
- Bullets barely scratch it: 80 hits to destroy it. The first hit says "ARMORED: USE A MISSILE, A RAM OR A HAZARD".
- It's hard to spin: a side ram spins it only when you hit it faster than 8 m/s sideways (5 m/s for other cars), so
  ramming it off the road takes commitment.

*Cost to change:* a few numbers.

**D39. Toughness and points.** Gunner 9 armor and 600 points, Dropper 9 and 600, Armored 12 and 900. The boss's
health comes from the mission file (mission 1: 180), and destroying it is worth 5000.
*Cost to change:* one number each.

**D40. Every enemy has its own colour and its own shape**, so colour is never the only clue. On the radar: the Gunner
is an orange-red ring with a dot, the Dropper a teal triangle pointing down, the Armored a steel-blue hexagon, and the
boss a pulsing white double ring around a gold ring. Crates are white boxes with a letter for the item (a star for the strike). Your missiles
are white dots; enemy missiles are red dots in a ring.
*Cost to change:* drawing only.

**D41. Limits on hurting the boss.**
- One hit can at most end the current phase: the boss's health stops at that phase's line.
- After a phase change the boss is shielded for 1.5 s, and its health bar says "SHIELDED".
- Gunfire does double damage while it's spun out (the bar says "ARMOR OPEN ×2") and a quarter in an Armored phase.
- Ramming does no damage, can't spin it, and can't push it off the road.

*Cost to change:* a few numbers.

**D42. Smoke works on the boss as on any car.** The design's boss table doesn't mention smoke. Inside the cloud the
boss loses sight of you, wanders and can't fire, like any other car (D27).
*Cost to change:* one line.

**D43. How the boss arrives.** At the end of the approach the boss appears 300 m ahead in the centre lane, with a 3 s
banner: "BOSS: BLACK HERON" and "DESTROY IT TO FINISH THE MISSION".
- It eases along 10 m/s slower than you (from 300 m to 140 m takes about 16 s) until you're within 140 m or you first
  hit it with anything. Only then does it start to fight.
- Enemy cars still on the road stay; no new waves come.
- It's never removed for being too far away. If it falls more than 120 m behind, it catches up at up to your speed
  plus 25 m/s (never faster than its top speed of about 197 mph); if it gets more than 600 m ahead, it waits for you.
  So it can't be lost. The one exception is while smoke blinds it: then it wanders like any blinded car (D42) and
  only starts catching up again once it can see you.
- After a respawn it is put 300 m ahead again and eases in the same way, with its health and phase unchanged.

*Cost to change:* a few numbers.

**D44. Enemy missiles.** Only bosses fire them, and mission 1's boss doesn't.
- A missile always closes on you at 25 m/s (about 56 mph), and is launched only when you're 60 to 160 m away, ahead or
  behind. That gives at least 2 s of warning every time (about 2.2 s at the closest).
- It follows your lane until 1 s before it arrives, then locks and goes straight, so a lane change in the last second
  dodges it.
- It hits if you're within 1.4 m of its line, for 25 damage.
- Only one is in the air at a time, and at most one every 6 s.
- You can shoot it down with the guns for 50 points.

The warning says where it is and counts down, for example "MISSILE INCOMING AHEAD RIGHT  IMPACT 1.6 s", with an
arrow at the screen edge. The ending always shows too: "MISSILE DODGED", "MISSILE HIT" or "MISSILE SHOT DOWN +50".
*Cost to change:* a few numbers.

**D45. Phase banners.** When the boss changes phase, a banner such as "PHASE 2 OF 3: PURSUER" shows for 2.5 s with a
one-line hint, for example "LET IT PASS, THEN SHOOT". The game's rules decide which warnings and banners are on screen,
not the drawing code, so the tests can prove each one shows when it should.
*Cost to change:* text only.

**D46. A test mission: the proving ground.** It has all six enemy kinds, a crate of every item, and a 3-phase boss,
the Glass Wasp: a Gunner phase, a Dropper phase, then a Pursuer phase that fires missiles. In each phase it shows the
warnings and signs of that kind of car (D67). You start with every weapon full. Open it with `--mission proving-ground`. The six real missions come in Plan 4, but you need somewhere to try all
of this now.
*Cost to change:* none; it's a separate file.

**D47. Mission 1's boss is the Black Heron.** 180 health. In phase 1 it drives like a Blocker until it's down to 60%
(72 gun hits). In phase 2 it drives like a Pursuer that pulls ahead after each attack (D49), down to 25%. In phase 3
it drives like a Runner, weaving fast (D50), to the end. It fires no missiles (the design keeps those for later
missions).
*Cost to change:* the mission file.

**D48. Repeated hits don't flood the messages.** Gunner bullets show "SHOT BY A GUNNER" at most once a second. The red
flash at the screen edge still shows for every hit.
*Cost to change:* one number.

**D49. A boss in a Pursuer phase pulls ahead after each attack**, where an ordinary pursuer drops back. It comes
alongside and rams, then overtakes in a lane away from you, pulls at least 60 m ahead and holds about 70 m for 2.5 s,
then drops back toward you (8 m/s slower than you) for the next attack. Your guns point forward, so each round leaves
the boss in front of them for a while. Ordinary pursuers behave as in Plan 1.
*Cost to change:* a few numbers.

**D50. A boss in a Runner phase weaves.** It hops to another lane every 1 to 1.8 s, and at once when shot, while
holding about 70 m ahead of you. So it stays a moving target instead of being passed.
*Cost to change:* a few numbers.

**D51. Any enemy closing from behind is warned.** An enemy within 60 m behind you and closing faster than 3 m/s shows
its kind, side and distance, for example "GUNNER BEHIND LEFT 30 m", with an arrow at the screen edge. The Plan 1
pursuer warning and the boss warning (within 120 m behind) stay as they were.
*Cost to change:* a few numbers.

**D52. Each warning is also recorded for sound later.** The moment a warning first appears, the game also notes it
as an event, so sound added later can follow exactly what the screen shows. The screen stays the master: what you
see is the full list.
*Cost to change:* none.

## While building Plan 2

**D53. Enemy cars look further ahead at speed.** The keep-clear rule (D28) was planned to look 25 m ahead. A car
closing fast needs more room to see the car, react and slow down, so it now looks at least 25 m ahead, and further the
faster it's closing: a Runner closing at 30 m/s (67 mph) looks 65 m ahead. So enemy cars change lanes earlier in
traffic.
*Cost to change:* one line.

**D54. When enemy cars crash into each other, you get the points.** If an enemy car is destroyed by hitting another
enemy car, the points are yours: your smoke, oil or rams caused it.
*Cost to change:* one line.

**D55. A boss that falls behind you comes back past you by itself.** In a Blocker or Armored phase the boss would
never overtake on its own, so it could sit behind you, out of reach of your guns. Now, once it's more than 10 m
behind, it overtakes at up to your speed plus 15 m/s, in a lane at least 2.5 m from yours, until it's 60 m ahead.
*Cost to change:* a few numbers.

**D56. The boss never drives through you.** When it overtakes, it uses the side with room. With no clear lane it
holds its line beside you, or drops back and crosses behind you. This took five rounds of review to get right.
*Cost to change:* hard; this is the most carefully tested driving rule.

**D57. Enemy caltrops can never wreck you.** They always leave you at least 1 armor. That matches the plan's "neither
can kill you by itself" (D31).
*Cost to change:* one line.

**D58. A missile in flight when you're wrecked ends as "MISSILE MISSED".** The same goes for one still in the air
when you respawn. So every missile launched has an ending on screen, and nothing launched before a wreck is still
flying when your new car starts.
*Cost to change:* a few lines.

**D59. Each oil slick, set of caltrops and wreck affects the boss only once.** Without this, two wrecks lying together
destroyed a full-health boss in 3 s, and a slow boss sitting on a slick was spun again and again.
*Cost to change:* a few lines.

**D60. A wreck can finish off the boss in its last phase.** Hitting a wreck costs the boss a tenth of its health,
under the normal limits (D41). In earlier phases that can at most end the phase; in the last phase it can destroy the
boss. You made the wreck, so it counts as yours.
*Cost to change:* one line.

**D61. At a respawn the boss comes back 300 m ahead**, with its health and phase kept, and eases in again (D43). Any
wreck, slick or caltrops where it reappears is cleared away, so it can't be hit the moment it arrives.
*Cost to change:* one line.

**D62. Boss banners come first.** The boss's arrival and phase banners take priority on screen: a strike result
never covers them (it still shows as a message). Whenever the boss is in range and its shield takes the
strike, the message ends with "- BOSS SHIELDED", whatever else the strike destroyed, for example "SATELLITE STRIKE: 2
DESTROYED - BOSS SHIELDED", and the strike banner says "BOSS SHIELDED" under the count (unless a boss banner is
showing, which comes first). So you always know why the boss wasn't hurt.
*Cost to change:* a few lines.

**D63. Every pursuer near you gets an edge arrow.** With pursuers on both sides, arrows show on both edges, so D14
still holds: a pursuer beside you is always shown.
*Cost to change:* a few lines.

**D64. With no missiles left, the lock line says "NO MISSILES"** instead of "NO TARGET IN SIGHT", and no LOCK box
is drawn on a car you couldn't fire at.
*Cost to change:* one line.

**D65. Speed figures for Plan 2.**
- Drawing one frame takes 1.5 ms on average (2.2 ms for the slowest one in twenty), with a crowded scene: 29 enemy
  cars of every kind, the boss, smoke, crates, missiles and a strike sweep. The limit is 8 ms. Plan 1's simpler scene
  took 0.55 to 0.84 ms.
- The stress test drew 12,002 frames, every 10 m of both mission 1 and the proving ground at three window sizes, plus
  a minimised window, with no failures. The boss and its health bar were on screen in one frame in three (4,002
  frames); the other frames showed the screen as it looks before the boss arrives.
- These figures leave out the graphics card's own work, as in D17. Turning the frames into pixels without a graphics
  card got about 3 times slower than in Plan 1, probably because of the big see-through smoke clouds, so please watch
  for stutter when smoke fills the view.

**D66. The F1 help box keeps clear of the weapon panel and the radar.** A test picture showed the help box
overlapping the right edge of the weapon panel by about 18 pixels at 1280×720. It now sits in the space between the
weapon panel and the radar, 10 pixels clear of each, as near the middle of the screen as it can. At 1280×720 that is
788 pixels wide (it was 800), and the longest line still has about 88 pixels to spare. Only in a tiny window, where
there is no room between them, does it go back to the middle of the screen on top of them.
*Cost to change:* a few lines.

**D67. A boss warns you like the car its phase drives like.** Until now a boss only ever showed "BOSS BEHIND". In a
test, its Gunner phase aimed at you for over 5 s in 20 s with no warning, and in its Pursuer phase every ram came
unwarned. Now each phase shows the same warnings and signs as that kind of enemy, in words that name the boss:
- Gunner phase: a gun port on its rear deck glows and "BOSS AIMING - LEAVE ITS LANE" shows. Its hits say "SHOT BY THE
  BOSS".
- Pursuer phase: from 3 m behind you to 4 m ahead, "BOSS LEFT" or "BOSS RIGHT" shows with an edge arrow (further back,
  "BOSS BEHIND" as before). It counts as a pursuer for the edge arrows (D63).
- Blocker phase: "BOSS BLOCKING AHEAD" with an arrow, when it's ahead in your lane.
- Dropper phase: it has two chutes, one each side of its exhaust, and they glow for 0.5 s before each drop. The message
  says "BOSS: OIL AHEAD" or "BOSS: CALTROPS AHEAD".

Two smaller changes to respawning: supply crates are always kept, wherever they are, so a respawn never costs you
supplies; and enemy bullets still in the air are cleared, as enemy missiles already were (D58), so none can hit your
new car.
*Cost to change:* a few lines.

## Open items after Plan 2

From Plan 1's "not urgent" list, these are now done: enemy cars collide with each other (D28), the oil recharge
shows in the weapon panel, and "BLOCKER AHEAD" has a side arrow. Still open from that list: F1 help doesn't pause the
game, you respawn where you crashed rather than a short way back (Plan 4), and mission 1 is fairly sparse (12 enemy
cars over 6 km), which is worth judging when you play it.

New, all small:
- A boss swerving off a wreck can be drawn passing through it for a moment. It makes no difference to play.
- Shift is one of the "next rear weapon" keys (D23). Pressing Shift five times quickly makes Windows offer Sticky
  Keys, which takes the focus away, so the game pauses (D12). Z does the same job, so Shift could be dropped.
- If you move to an outer lane while a boss in a Pursuer phase is dropping back toward you, it can cross in front of
  you about 8 m ahead.
- An ordinary pursuer (Plan 1) still has the flaw the boss had when you're in an outer lane: with no room beside you
  on its side, it can end up almost straight behind or ahead of you instead of beside you. This was fixed for the boss
  only.

Coming in later plans:
- Plan 3: a test that the satellite strike spares civilians and police (it already only targets enemies), how
  civilians and police react to smoke, and heat from weapons.
- Plan 4: missions 2–6 (and with them enemy missiles in missions 5 and 6), the briefing screen with the boss drawing
  and your loadout, the mission bonus, respawning a short way back, and the text-size setting.

## Checks only you can do (Plan 2)

1. Play mission 1 to the Black Heron and beat all three phases. Tell me whether the slow arrival from 300 m (about
   16 s to close in, D43) and the Pursuer phase that pulls ahead after each attack (D49) feel right.
2. Start the proving ground: from `C:\repos\NightPursuit`, run
   `dotnet run -c Release --project src/NightPursuit.App -- --mission proving-ground`. Then:
   - smoke a pursuer and watch it wander in the mirror;
   - drop caltrops in front of a runner;
   - lock onto a car and fire a missile;
   - use the satellite strike;
   - dodge a boss missile by changing lanes in its last second, and shoot another one down. The Glass Wasp fires
     missiles only in its third phase.
3. Check that every warning and banner is easy to read without sound, and that the new enemy colours are easy to tell
   apart: orange-red Gunner, teal Dropper, steel-blue Armored, the white-and-gold boss, and the teal Dropper from the
   yellow Blocker.
4. Two things the test pictures couldn't show me well:
   - the Dropper's chute glowing just before a drop (too small to make out at 90 m);
   - the Glass Wasp's gun port and its two chutes in its Gunner and Dropper phases (D67), which no test picture shows
     yet;
   - whether the game stays smooth with smoke clouds on screen (D65).
