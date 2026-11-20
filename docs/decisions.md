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

**D23. Weapon keys.** Tab picks the next rear weapon (Z or Shift at first; see D68), X or Ctrl drops it, F fires a missile and V calls
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

## After your third playtest (2026-11-19)

**D68. Tab picks the next rear weapon, not Z or Shift.** You found Z hard to reach and sometimes hit X (drop)
instead. Tab now picks the next rear weapon, and Z no longer does anything. At your request Shift was dropped too:
pressing it five times quickly made Windows offer Sticky Keys, which paused the game. The F1 help and the weapon
panel's caption ("X drop   Tab next") say Tab. The game takes Tab for itself, so it never moves the window's focus.
Alt+Tab still switches windows as usual, and the game pauses itself (D12). The gamepad is unchanged (X button).
*Cost to change:* one line in the key table and two captions.

## Open items after Plan 2

From Plan 1's "not urgent" list, these are now done: enemy cars collide with each other (D28), the oil recharge
shows in the weapon panel, and "BLOCKER AHEAD" has a side arrow. Still open from that list: F1 help doesn't pause the
game, you respawn where you crashed rather than a short way back (Plan 4), and mission 1 is fairly sparse (12 enemy
cars over 6 km), which is worth judging when you play it.

New, all small:
- A boss swerving off a wreck can be drawn passing through it for a moment. It makes no difference to play.
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

## Plan 3 calls (before building)

The design left these open, so I picked a default for each before building Plan 3 (traffic, police and heat), and
the game follows them. I checked every entry below against the finished code. A few things changed while building
(where police appear, the patrol ground's starting heat, the three-lane rule, the colours); where the build ended up
different from the plan, the entry says what the game really does.

**D69. Civilians and police are cars like the enemies.** They follow the same car rules as the enemy cars (driving,
bumping, smoke, the mirror and the radar), so nothing had to be built twice. There are five new kinds: the civilian
car, the patrol car, the police cruiser, the interceptor and the armored SUV. Each kind belongs to a side, the
civilians or the police, and the side decides who scores, who warns you and what your missiles and strike can aim at.
*Cost to change:* hard; everything in this plan is built on it.

**D70. Civilians cost you no points (your ruling, see D96).** Hurting a civilian costs your car armor from the bump,
as any contact does, and raises heat, but it never takes points off. Civilians and police are worth no points when
they're destroyed, either.
*Cost to change:* a few lines.

**D71. Only what you did counts against you.** A civilian or police car that is destroyed counts against you only if
you hurt it in the last 5 s: your bullets, a bump that was your fault, or your own oil or caltrops. A crash caused by
an enemy or by another car costs you nothing, and says "CIVILIAN CRASHED" or "POLICE CAR CRASHED". Whose fault a bump
is:
- you run into the back of a car: your fault (with two exceptions for police, D99);
- side by side: your fault only when your own steering moved you into it, faster than it moved into you (D98);
- a car that runs into the back of you, swerves into you or rams you costs you nothing. An interceptor ramming you
  never gives heat.

*Cost to change:* one number for the 5 s; the fault rule is a few lines.

**D72. How often a hit counts.**
- A civilian: +10 heat at most once a second for each car, whether you hit it with bullets or bump it. So holding the
  guns on one adds at most +10 a second, plus +25 once if it's destroyed.
- A bump with a police car: +15, at most once a second for each car.
- A bullet hitting a police car: +5 every time, as the design says. Its message, "POLICE CAR SHOT +5 HEAT", shows at
  most once a second, so the list doesn't flood.

*Cost to change:* one number each.

**D73. Smoke.**
- Civilians aren't blinded by it. Inside a cloud they slow to 60% of their usual speed and keep their lane.
- Police are blinded exactly like enemies (D27). "POLICE LOST YOU IN THE SMOKE" shows (or "INTERCEPTOR", or "POLICE
  SUV"), and they wander. A blinded police car can't bust you or take a new place in a box-in, and a rolling roadblock
  with a blinded SUV breaks up. So smoke is a real way to shake a pursuit.

*Cost to change:* one line each.

**D74. Missiles and the satellite strike spare civilians and police.** They only ever aim at enemies. That was already
true; new tests now prove it, as the design's checks (§11) ask.
*Cost to change:* none.

**D75. Traffic.**
- Civilians all drive your way, in lanes, at 25–35 m/s (56–78 mph).
- New ones appear 1250–1450 m ahead, beyond the 1200 m the view draws, so they never pop into sight. While you drive
  slower than 25 m/s (56 mph), new ones come from 400–450 m behind instead, at 30–35 m/s (67–78 mph), and catch
  you up.
- They're removed when they're more than 1500 m ahead or 500 m behind.
- A new car never appears within 40 m of another car in its lane, within 60 m of a wreck or roadblock, or within 30 m
  of anything lying in its lane.
- At the start, the road from 400 m behind you to 1450 m ahead is filled at once (never within 60 m of you).

The mission file says how busy the road is: `"traffic": { "carsPerKm": n }`, from 0 to 12 (no section means no
traffic). The game keeps about twice that many cars in the 2 km around you. Mission 1 has 3 per km, so about 6 cars.
*Cost to change:* a few numbers.

**D76. Police settings in the mission file.** `"police": { "presence": p, "startingHeat": h }`.
- `presence` runs from 0 to 2: 0 is no police, 1 is normal, 2 is twice as many. A mission with no police section has
  no police; a police section that leaves `presence` out gets 1.
- `startingHeat` runs from 0 to 100, and is 0 if left out. With presence 0 it must be 0, since no police would ever
  come; a file that gives one is refused with "police.startingHeat must be 0 when police.presence is 0".
- Mission 1 has presence 1 and starts at heat 0. The patrol ground starts at 85 (D89, D100).

Police only come onto the road once your heat reaches Patrol (50). So in mission 1 you meet them only if you push your
heat that high, for example by hurting civilians.
*Cost to change:* none.

**D77. How heat dies down.**
- After 5 s with no offence, heat falls 2 a second, or 4 a second when no police car is near. "Near" means within
  300 m along the road, or any police car that is chasing you and can see you (not blinded), however far away.
- Heat stays between 0 and 100.
- Levels hold on. A level starts at its threshold (Watched 25, Patrol 50, Pursuit 70, Manhunt 90), but ends only when
  heat falls 5 below it (under 20, 45, 65 or 85). Patrol, Pursuit and Manhunt each last at least 20 s once reached.
  Heat comes down one level at a time.
- Without the hold, a pursuit could end before the cruisers had even arrived. For example, on the patrol ground
  (starting at 85, Pursuit), if you commit no offence at all, Pursuit lasts 20 s from when you move off and then drops
  to Patrol.
- At the start of a mission with police, nothing happens until you first drive faster than 5 mph: the heat doesn't
  fall, the time in its level doesn't count, nothing chases you, no new police come, and no speed traps or roadblocks
  are placed. So you can read the start help in peace. A mission that starts at Patrol or above does have its patrol
  cars on the road from the first moment, spread from 400 m behind you to 1450 m ahead, driving as traffic, so none
  pops into view when you move off. Chasers then come from 150–250 m behind you. Offences still add heat. A respawn
  doesn't hold the heat again.
- A mission that starts at Patrol or above says its level once at the start, for example "HEAT: PURSUIT" (D87).
- A bust sets the heat straight to 50 instead (D84).

The design's check "three civilian hits reach Patrol level" doesn't work from 0 (D97).
*Cost to change:* a few numbers.

**D78. Speed traps.**
- A camera post at one edge of the road (either side, at random) with its limit on a sign: 60, 70 or 80 mph, also at
  random.
- The first one stands 700 m ahead as soon as heat reaches Patrol, then one every 1500 m of road while heat stays at
  Patrol or above (D109 keeps that spacing when you come back into Patrol). They keep coming in the boss stage.
- Passing one above its limit: +8 heat once, "SPEED TRAP 112 IN A 70 +8 HEAT", and its sign turns red and says CAUGHT
  (D112). Under the limit: "SPEED TRAP: UNDER THE LIMIT".
- The sign is drawn from 400 m (it fades in between 400 and 350 m), and "SPEED TRAP 70 MPH 380 m" shows from 400 m
  until you pass it. The design asks for at least 300 m.
- It never blocks anything: you can't hit it.

*Cost to change:* a few numbers.

**D79. Roadblocks.**
- Barriers across two lanes, always leaving one lane open. Green chevrons on the road mark the gap, with the word GAP
  from 400 m. The barriers are drawn from 450 m.
- "ROADBLOCK 380 m - GAP LEFT" (or CENTRE, or RIGHT) shows from 400 m, with an arrow pointing to the gap when you're
  not already in it.
- The first one stands 800 m ahead as soon as heat reaches Patrol, then one every 3000 m while heat stays at Patrol or
  above, and only one at a time, parked or rolling (D105, D109).
- Its open lane never has a wreck within 150 m of it. If every lane has one, it tries again 500 m later.
- None in the boss stage: any left on the road is removed the moment the boss appears.
- Hitting one does 25 damage, drops you to 5 m/s and gives +15 heat once ("ROADBLOCK +15 HEAT"). Then you slide along
  it at 5 m/s to get round, so you are past it well inside the 3 s busted countdown.
- Other cars see it from 120 m, or from 5 s away at their speed if that is further, and steer for the gap. Any other car
  that touches a barrier is wrecked.
- So that they have time to do that, a roadblock is never put down with a car in a lane it would block, from 150 m
  before it (or 5 s at that car's speed, if further) up to the barrier itself. The gap goes in a lane where no car is
  in the way: usually a car's own lane. If no lane clear of wrecks will do, it waits and tries again at the next
  moment, still 800 m ahead of you. In 120 test starts on the patrol ground it never had to wait. (Before this, the
  first roadblock on the patrol ground landed on a civilian in 12 of 20 test runs; now in none of 20.)
- If a wreck lands in its gap lane, anywhere from 150 m before it to 30 m after it, the roadblock is removed at once:
  "ROADBLOCK CLEARED".
- Within 150 m before a roadblock (or 5 s at your speed), chasing police drop back behind you, so none of them is
  stopped in the gap.
- You can also go round the end of a barrier on the shoulder (D102).

*Cost to change:* a few numbers.

**D80. Patrol level.**
- Patrol cars drive in the traffic at 28–32 m/s (63–72 mph) and ignore you. Their light bars flash all the same
  (HT-4).
- The police in the 2 km around you are topped up with patrol cars to 2 × presence: 2 in mission 1, 3 on the patrol
  ground. New ones appear 1250–1450 m ahead, like traffic.
- They keep their own lanes like ordinary traffic. At this level the three-lane rule never moves or holds them back
  (D103).
- There are no police warnings at Patrol level: the light bars and the radar show them.
- Below Patrol no new police come at all; any already on the road stay until they're left behind.

*Cost to change:* one number.

**D81. Pursuit level.**
- Every patrol car within 600 m of you starts chasing.
- Cruisers join from behind until there are 2 × presence chasers, at most 3 (2 in mission 1, 3 on the patrol ground;
  patrol cars that switched count). They appear 150–250 m behind you (D100), at your speed + 15 m/s, and close in at
  up to 20 m/s faster than you.
- The two nearest take places in a box-in:
  - **Front** gets past you, takes your lane about 12 m ahead and, while within 25 m ahead of you, slows to your
    speed − 4 m/s, never below 8 m/s (18 mph).
  - **Side** holds level with you in the lane beside you, at your speed but never slower than 3 m/s, so it rolls on
    past you if you stop. It never steers within 2.7 m of your line.
- The rest **Trail** you: in your lane about 20 m behind, more at speed (about 47 m at 100 mph) so it can stop if you
  brake hard. When you're below 5 m/s (11 mph) it stops 6 m behind you, bumper to bumper. A Trail car that is more
  than 30 m ahead of you (it was the Front car, say) never goes slower than 8 m/s (18 mph) while it drops back, so it
  drives on rather than stop dead in its lane ahead of you when you're slow (it still stops short of a wreck or
  roadblock in its way).
- They never ram at Pursuit level.

D114 says how the box-in stays safe when you brake.
*Cost to change:* a few numbers.

**D82. Manhunt level.**
- **Interceptors** are low and long, with a top speed of 80 m/s (179 mph); you top out near 67 m/s (150 mph). As many as
  the presence (rounded, D95), at most 2: 1 in mission 1, 2 on the patrol ground. They come from 150–250 m
  behind and attack like a Plan 1 pursuer: they come alongside, ram you and drop back (D106).
- **Armored SUVs** are tall and boxy, with 30 armor (three times a cruiser's) and a top speed of 82 m/s (183 mph).
  With presence 1 or more, and never in the boss fight, a pair comes from 150–250 m behind, 35 m apart, at your
  speed + 15 m/s, and passes you. Once both are at least 60 m ahead they form a **rolling roadblock**: side by side
  across two lanes, about 40 m ahead, leaving one lane open. That is the outer lane farthest from you, or, if you're in
  the middle lane, an outer lane picked at random (D105). Once you're within 40 m of both, they ride at your
  speed − 6 m/s, so you close on them gently and must take the gap or brake.
- A rolling roadblock gets the same warning as a parked one, "ROLLING ROADBLOCK 60 m - GAP RIGHT" with an arrow, a
  green chevron in its gap, and the same radar mark.
- While the pair is forming, or once it has formed with an SUV within 80 m of you, the Side car drops back to Trail.
  It doesn't take the Side place back until that pair is gone, so it doesn't swing back and forth across a lane.
  With a formed pair within 80 m, the Front car drops back too: the pair is the front of the box then.
- At most 6 police in the 2 km around you, with room given first to the SUVs, then the interceptors, then the
  cruisers, then the patrol cars. To make room, a police car lower down that list is removed, but only where you
  can't see it (more than 1200 m ahead or 300 m behind), patrol cars first. For an interceptor that can also be a
  cruiser, and for the SUVs a cruiser or even an interceptor; a new cruiser only ever replaces a patrol car. Until
  there is room, a new police car waits rather than go over 6.

*Cost to change:* a few numbers.

**D83. When heat drops a level, the police back off.** Below Pursuit every chasing car, and below Manhunt the
interceptors and SUVs, go back to patrol driving at 25 m/s (56 mph), so they fall away behind you, and a rolling
roadblock breaks up. "POLICE BACKING OFF" shows. Interceptors and SUVs that have backed off never join a chase at
Pursuit level.
*Cost to change:* one number.

**D84. Busted (HT-6).**
- It can only happen at Pursuit or above.
- You're below 5 mph with a police car that is chasing you within 15 m of you (in a straight line, centre to centre)
  for 3 s. Trail and Front cars, interceptors and SUVs count. These don't:
  - the Side car: it rolls on past you at 3 m/s when you stop, so on its own it busted you whenever you braked to a
    stop to get out of a box-in (in all 10 test runs that braked to a stop from 18 mph);
  - a patrol car, or a chaser that has backed off (D83): they are just traffic, and aren't chasing you;
  - a blinded or spun police car.
- "BUSTED IN 3", then 2, then 1, shows large in red on a dark box from the very first moment, with "SPEED UP TO BREAK
  FREE" under it.
- The countdown stops when you go above 5 mph, when no chasing police car is within 15 m any more (it drove on, it
  was blinded by smoke or spun out, or it became the Side car), or when the heat drops below Pursuit. Whichever it is,
  "BROKE FREE" shows. (Being wrecked doesn't count as breaking free.)
- Nothing counts while you're wrecked or respawning, or while the game is paused. It doesn't count until you've first
  driven faster than 5 mph (D101).
- A bust costs a spare car, as a wreck does: a "BUSTED" banner and "BUSTED - YOU LOSE A SPARE CAR".
- When you respawn after a bust, your heat is set straight to 50 (Patrol), without the 20 s hold, and "HEAT SET TO 50:
  PATROL" shows, so you aren't busted again at once. A wreck leaves your heat as it was.

*Cost to change:* a few numbers.

**D85. Moving police never fill all three lanes near you (HT-7).** As built:
- A police car covers a lane when its body overlaps it, so one halfway between two lanes covers both.
- The cars counted are police driving normally (not spun out or wrecked; blinded ones count) within 45 m of you along
  the road. The plan said 60 m; a check before building showed that 60 m couldn't work.
- A police car that near may not steer anywhere that would cover the third lane, at any moment on the way.
- One farther out that would make the third lane is held back, beyond 55 m plus more room at speed (about 145 m at top
  speed), until a lane frees up. Its own lane is never taken from it: it is held back by speed instead.
- When a third lane would be covered, the Side car drops back to Trail rather than take its place.
- The rule never steers a car into another car, into you, or into a wreck or roadblock.
- Patrol cars just driving in the traffic count toward the three lanes, so chasing cars give way to them, but below
  Pursuit the rule never moves or holds a patrol car (D103).

Two rare exceptions, where bumps push cars about, are in D104 and the open items.
*Cost to change:* hard; with the box-in, this is the most carefully tested part of this plan.

**D86. How civilians and police look.**
- **Civilians:** a dim neutral grey (#7F8C99), each one a sedan, a hatchback or a van, so they never look like the
  bright enemy cars.
- **Police:** a pale silver-blue body (#C9D6EA) with a light bar. Its two halves are always opposite colours, blue
  (#2E6BFF) and red (#FF2E4D), and swap every 0.2 s, so both colours always show. Patrol cars and cruisers are sedans,
  interceptors are low and long with a wide stance, and armored SUVs are tall and boxy with shoulders. In the mirror a
  police car has a two-colour line on top that flashes with the light bars.
- **Radar:** civilians are small grey dots. Police are squares with a white outline and a dark bar across them,
  filled blue or red in time with the light bars (larger for an SUV). A speed trap is an amber S at the road edge ("S!"
  in red once it has caught you). A roadblock, parked or rolling, is a red bar across the lanes it holds with a green
  tick in the gap.

*Cost to change:* drawing only.

**D87. The heat meter.** It sits in the right-hand column under the score and armor (that block grows from 156 to 200
units tall to make room).
- It shows "HEAT 85", the level name written in its own colour on the right, and a bar from 0 to 100 in five coloured
  parts, with ticks at 25, 50, 70 and 90. The colours are in D113.
- When heat rises, the bar's outline turns thick and white and blinks twice a second for 1 s.
- Rising to Patrol or above shows a banner:
  - "POLICE PATROLS" / "SPEED TRAPS AND ROADBLOCKS AHEAD";
  - "POLICE PURSUIT" / "DON'T LET THEM STOP YOU";
  - "MANHUNT" / "THEY WILL RAM YOU - SHAKE THEM WITH SMOKE".
- Every level change also gives exactly one message, such as "HEAT: PURSUIT", "HEAT: WATCHED - THE POLICE ARE TAKING
  NOTE" or "HEAT DOWN: PATROL". That's because a banner can be held back: by a boss banner or the BUSTED banner (D62),
  or when another banner went up less than 1 s before.
- A mission that starts at Patrol or above gives that level's message once at the start ("HEAT: PURSUIT" on the
  patrol ground), with no banner, since the level didn't change.

*Cost to change:* drawing and text only.

**D88. Every offence says what it cost.** "CIVILIAN HIT +10 HEAT", "CIVILIAN DESTROYED +25 HEAT", "POLICE CAR HIT +15
HEAT", "POLICE CAR SHOT +5 HEAT", "POLICE CAR DESTROYED +40 HEAT", "SPEED TRAP 112 IN A 70 +8 HEAT" and "ROADBLOCK +15
HEAT". A roadblock also shows "HIT A ROADBLOCK", as other damage does. When it wasn't your fault: "CIVILIAN CRASHED" or
"POLICE CAR CRASHED", and no heat.
*Cost to change:* text only.

**D89. A test mission: the patrol ground.** Open it with `--mission patrol-ground`.
- Heavy traffic (6 cars per km, so about 12 around you), police presence 1.5, and a starting heat of 85, which is
  Pursuit (the plan said 75; see D100).
- Two waves of two Runners, a crate of every item (oil, smoke, caltrops, missile, strike and armor), and a 2-phase
  boss, the Night Heron: 120 health, a Blocker phase, then a Runner phase. The approach is 5 km.
- You start with 5 oil, 3 smoke, 3 caltrops, 2 missiles and 1 strike.
- Right at the start, one bullet on a police car (+5) takes you to Manhunt.
- The game runs while the start help is showing, but the heat and the police wait until you first drive faster than
  5 mph (D77): the heat stays at 85, its 3 patrol cars drive as traffic, and the chasers come from behind the moment
  you move off. Without an offence, Pursuit then
  lasts 20 s.

The real missions 2–6 come in Plan 4.
*Cost to change:* none; it's a separate file.

**D90. Heat, busts, traps and roadblocks are recorded for sound later**, as warnings are (D52): every heat change, every
level change, a bust, a speed trap catching you and a roadblock hit. A destroyed car now also records which side it
was on. The screen stays the master.
*Cost to change:* none.

**D91. Police warnings.** From Pursuit up only (at Patrol level the light bars and the radar show them):
- "POLICE LEFT" or "POLICE RIGHT", with an arrow at that edge of the screen, for a police car from 3 m behind you to
  4 m ahead and within 6 m to the side;
- "POLICE BEHIND 80 m", with LEFT or RIGHT when it isn't in your lane (D111), for the nearest one 3 to 120 m behind
  you.

Roadblocks, rolling roadblocks and speed traps warn at any level, from 400 m (D78, D79, D82). Civilians never raise a
warning: only enemy cars count for the "closing from behind" warning (D51). The police warning words are a lighter
blue (D110).
*Cost to change:* a few numbers.

**D92. Enemies and police ignore each other.** Enemy cars don't attack the police and the police don't chase enemies.
Enemy bullets still never hit any car. If they bump, the usual car-to-car rules apply (D28), and none of it costs you
heat.
*Cost to change:* none to keep.

**D93. Civilians and patrol cars see you ahead of them.** The old keep-clear rule (D28) ignored you, so civilians and
police would have run into you whenever you were slow: after a roadblock or a wreck, after a respawn, or while boxed
in. Now they treat you as a car ahead: they change to a free lane, or follow at your speed − 2 m/s. (Chasing police
have their own rules for keeping off you, D81.)
*Cost to change:* a few lines.

**D94. Missions can't misuse the new kinds.** An enemy wave, or a boss phase, must be one of the six enemy types. A
mission file asking for "Civilian" or a police kind is refused with a clear message, for example "'Civilian' is not an
enemy type (Runner, Blocker, Pursuer, Gunner, Dropper, Armored)".
*Cost to change:* none.

**D95. Counts round halves up.** So presence 1.25 gives 2.5 patrol cars, rounded to 3, not 2, and presence 0.5 gives
1 interceptor, not 0. (The usual computer rounding sends halves to the even number, which would give 2 and 0.)
*Cost to change:* one line.

## While building Plan 3

**D96. Your ruling: civilians cost no points (2026-11-19).** Hurting civilians costs only heat, and your car's normal
armor damage from the contact. Civilians and police are worth 0 points. This replaces the design's "costs points" for
civilians (CIV-3) and "penalties for civilians" in the score (MSN-5). You said you'll judge the police in play and may
retune the heat levels afterwards. D70 is how the game does it.
*Cost to change:* a few lines.

**D97. "Three civilian hits reach Patrol level" needs a head start.** The design's example check (§11) only works from
heat 20 or more: three hits of +10 from 0 give 30, which is Watched. I kept the HT-2 numbers as the design gives them.
The test starts at 25 and reaches Patrol at 55; a second test checks that from 0 the same three hits give Watched at 30.
*Cost to change:* one number, in the design or in the heat table.

**D98. Whose fault a side bump is.** Only your own steering counts: your speed times the sideways angle of your car's
nose. A bump is your fault only when that is above 0.5 m/s (about 1 mph) and more than the other car is moving into you.
Being pushed or shoved into a car (by an enemy, or by enemy oil) never counts, nor do small lane-keeping corrections,
and a car running into the back of you never counts.
*Cost to change:* one number.

**D99. Two police bumps that aren't your fault, and when they still cost you.**
- Running into the back of a police car isn't your fault when it's going slower than its cruising speed (it was put
  there, or held back, to slow you), or when it's the Front car boxing you in. Without this, holding the gas behind
  the Front car gave +15 every second, and Pursuit became Manhunt within about 2 s of touching it.
- The bump still counts as yours for 5 s, though: if your ramming destroys that police car within 5 s, it costs +40.
  So ramming the Front car to death costs +40 heat (HT-7 says "ramming raises heat"). Braking and swerving stay free
  ways out.
- Running into the back of a police car that has backed off and is still slowing down to 25 m/s is your fault: it's
  braking ahead of you, still faster than its cruising speed.
- Every car chasing you (cruisers, interceptors, SUVs, and patrol cars once they chase) has the same cruising speed,
  30 m/s (67 mph), whatever your speed when it came, so the first rule doesn't depend on that. A patrol car cruises at
  its own 28–32 m/s, and one that has backed off at 25 m/s. Their chasing speeds come from their places in the chase
  (D81, D82), not from this. A blinded chaser slows to 60% of it, 18 m/s (40 mph), as in D27.

This bends HT-3's "ramming them always raises heat" in those two cases; say if you want it back.
*Cost to change:* one rule.

**D100. The police arrive closer, and the patrol ground starts hotter.** Cruisers, interceptors and SUVs appear
150–250 m behind you, not 350–450 m as planned. From 350–450 m a cruiser took about 27 s to reach you (about 44 s near
top speed), longer than Pursuit's 20 s hold, so a pursuit could end before it arrived. 150–250 m is still beyond the
120 m at which "POLICE BEHIND" shows. If a mission starts at Pursuit or above, all its chasers are there from the
moment you first move off (D77). The patrol ground starts at heat 85, not 75. Both are tuning numbers for you to judge.
*Cost to change:* two numbers.

**D101. You can't be busted before you've moved.** The busted countdown doesn't start until you've first driven faster
than 5 mph since the mission start or your last respawn. The game runs while the start help is up, so without this a
police car could have stopped behind you and busted you while you were still reading. At the mission start the heat and
the police wait for that first move too (D77).
*Cost to change:* one line.

**D102. Going round a roadblock on the shoulder is allowed.** The barriers end 7.3 m from the middle of the road; the
road's edge is at 7.5 m and the guard rail at 12 m. You can drive round the end of a barrier off the road. That slows
you and shakes the car, as any driving off the road does, but costs no damage and no heat, as long as you keep off
the guard rail. The design says the gap may be "a lane or the shoulder" (HT-7).
*Cost to change:* none to keep.

**D103. At Patrol level, patrol cars are just traffic.** They keep their own lanes, and between them they can happen to
cover all three lanes at once. They never box you in, ram you or bust you (busting needs Pursuit). The "never all three
lanes" guarantee (D85) is for police chasing you, at Pursuit and above, and its purpose is that you always have a way
out ahead. An earlier version moved patrol cars out of the way, and they swerved into your lane and ran into you (15
hits in 30 s in one test). They still count toward the three lanes, so chasing cars give way to them.
*Cost to change:* a few lines.

**D104. A rare moment behind a rolling roadblock.** When you're stopped dead behind a rolling roadblock with police
around you, a bump can knock a police car into the third lane behind you for about 5 s. For that moment the police
cover all three lanes, but only behind you: the open lane ahead stays clear, so you still have a way out. I accepted
it; here HT-7 means a way out ahead of you.
*Cost to change:* hard to remove completely; it comes from cars bumping.

**D105. How the rolling roadblock works.**
- When the pair is sent, it draws which outer lane it would leave open if you were in the middle lane. The gap is
  locked when the pair forms, from where you are then: the outer lane farthest from you, or that drawn lane if you're
  in the middle.
- The SUVs pass you in a lane that is neither yours nor the one they'll leave open, so they never block the gap on the
  way.
- A formed pair breaks up when an SUV has sat in the gap for 2 s (pushed there, or stuck), when an SUV within 60 m of
  you has been off its lane for 3 s (after 4 s to settle in), when an SUV is wrecked, blinded or gone, or when a wreck
  or roadblock blocks an SUV's lane, or the open lane, within 120 m ahead of an SUV (with the open lane blocked you'd
  have no way past). Both SUVs then chase you as Trail.
- After a pair breaks, a new one comes no sooner than 20 s later, and only once both old SUVs are gone (wrecked, left
  behind, or backed off). Never more than 2 SUVs chase you at once; SUVs that have backed off don't count, so more
  than 2 can be on the road.
- One roadblock at a time: no pair is sent while a parked roadblock is ahead of you or less than 300 m behind you, and
  no roadblock is parked while a rolling one is on the road.
- While a pair is near, the Front car drops back and the box-in stays on the side away from the gap.

Before these rules, a formed pair could get stuck in its own gap (in 16 test runs out of 72).
*Cost to change:* a few numbers.

**D106. Interceptors stay out of the gap and out of your back.**
- They never sit in a rolling roadblock's open lane; one that finds itself there (spun into it, say) ends its attack
  and moves out.
- They switch to your other side when a car blocks their line of attack.
- They never attack from a side where the road edge would squeeze them into your lane. (Ordinary pursuers still have
  that flaw; see the open items.)
- In your lane behind you they never close faster than they could stop 1 m short of you, even if you brake as hard as
  you can, so they can't run into the back of you.
- While a rolling roadblock is forming they hold off at least 80 m behind you and don't ram, so the SUVs have room.

In the tests they ram about twice a minute. Please judge whether that is enough.
*Cost to change:* a few numbers.

**D107. A police car stuck in the gap leaves by changing speed.** If a police car within 60 m of you ends up in a
rolling roadblock's open lane with no free lane to move into, it drops back behind you (at your speed − 4 m/s) or, if
it's ahead of you, pulls away (at your speed + 4 m/s) until it's clear.
*Cost to change:* one number.

**D108. Civilians pull over only where there's room.**
- At Pursuit and above, a civilian within 150 m of you or of a chasing police car heads for the shoulder at 12 m/s
  (27 mph). It goes back to its lane 3 s after the chase has passed.
- It only heads for a side whose path is clear: no car in the lanes between, from 15 m behind it to 40 m ahead. With
  neither side clear, it holds its lane and slows down.
- A civilian is shaken when your bullets hit it or you run into the back of it (whoever's fault that was): it heads
  for the shoulder at half speed for 3 s. A side bump, or a bump from another car, doesn't shake it.
- Civilians also move aside from a car closing fast from behind (about 2.5 s from reaching them) or when you fire at
  them in your line from within 80 m: they change lane if one is free and slow to 70%, or else keep their lane and
  slow to 60%.

*Cost to change:* a few numbers.

**D109. Roadblocks and speed traps keep their spacing.** A roadblock that comes due while an earlier one is still on
the road is placed a full 800 m ahead once it can be, never nearer. When heat comes back up into Patrol, speed traps
stay at least 1500 m apart (a trap that isn't due yet keeps its place), and the roadblocks start again with one 800 m
ahead.
*Cost to change:* a few lines.

**D110. Police warning words are a lighter blue (#6FA0FF)**, so they're easy to read on the dark road. The light bars
keep the strong blue (#2E6BFF).
*Cost to change:* one colour.

**D111. "POLICE BEHIND RIGHT 80 m" includes the side**, like the other "behind" warnings (D51). It says just "POLICE
BEHIND 80 m" when the car is in your lane.
*Cost to change:* none.

**D112. A speed trap that caught you says so in words.** Its sign turns red with "CAUGHT" under the limit, and its
radar mark changes from "S" to "S!", so colour isn't the only cue.
*Cost to change:* drawing only.

**D113. Heat level colours:** Calm green (#39FF88), Watched yellow (#FFE14D), Patrol amber (#FFC13B), Pursuit orange
(#FF8A1F), Manhunt red (#FF3B3B). A review found them one step out of place at first; they now match the plan. The
level name is always written beside the bar too.
*Cost to change:* five colours.

**D114. The box-in stays safe when you brake hard.**
- The distances grow with speed. A Trail car keeps about 20 m behind you, more at speed (about 47 m at 100 mph). A car
  held back by the three-lane rule stays beyond 55 m plus extra room at speed (about 145 m at top speed). Either can
  still stop if you brake as hard as you can.
- Places in the box stick: a car keeps its place while it's chasing, and Front and Side swap only when Side gets more
  than 10 m ahead of Front.
- Side never goes slower than 3 m/s, so it rolls past you if you stop. If you stop right beside it, rolling at 3 m/s it
  stays within 15 m of you for up to about 5 s, but the Side car never counts toward a bust (D84), so braking to a stop
  is a safe way out of a box-in. A Trail car that stops behind you still counts.

*Cost to change:* a few numbers.

**D115. The tests can look inside the game's rules.** This is a note for programmers: the test project is allowed to
reach parts of the game's rules that are otherwise private, so the tests can check them directly. It changes nothing
in play.
*Cost to change:* none.

**D116. Speed figures for Plan 3.**
- Drawing one frame takes 2.245 ms on average (4.06 ms for the slowest one in twenty). The scene is crowded: 25 enemy,
  civilian and police cars of every kind, 8 more civilians, 3 more police, heat 80, a roadblock 300 m ahead, a speed
  trap 200 m ahead, the boss, smoke, crates, missiles and a strike sweep. The limit is 8 ms; Plan 2's scene took
  1.5 ms.
- One frame in 600 took 24 ms. It looks like a one-off, perhaps memory being tidied up, or the first draw after the test
  rebuilds its scene (it does that every 60 frames). I didn't look into it further. Please watch for a single hitch.
- The stress test drew 18,003 frames of mission 1, the proving ground and the patrol ground at three window sizes,
  plus a minimised window, with no failures. The boss bar was on screen in 6,003 of them.
- The stress test now takes about 68 minutes, 47 of them for the very wide 2560×600 window. You can decide whether to
  shorten it for later plans.
- As in D17, these figures leave out the graphics card's own work.

*Cost to change:* none.

## Open items after Plan 3

Still open from the earlier lists:
- F1 help doesn't pause the game.
- You respawn where you crashed, not a short way back (Plan 4).
- Mission 1 was "fairly sparse" (12 enemy cars over 6 km). Traffic partly answers that: it now has 3 civilian cars per
  km (about 6 around you at any time) and police at presence 1. But it starts at heat 0, and the police only come once
  your heat reaches Patrol (50), so a clean run meets no police. The enemy count is unchanged. Still worth judging when
  you play it.
- A boss swerving off a wreck can be drawn passing through it for a moment.
- If you move to an outer lane while a boss in a Pursuer phase is dropping back toward you, it can cross in front of
  you about 8 m ahead.
- An ordinary pursuer (Plan 1) still has the outer-lane flaw: with no room beside you on its side, it can end up almost
  straight behind or ahead of you. The boss was fixed in Plan 2, and interceptors avoid that side (D106).

New from Plan 3:
- Police occasionally crash into a roadblock (0 to 2 times in 90 s in the tests). It costs you no heat ("POLICE CAR
  CRASHED").
- Right after cars bump at Manhunt level, police can briefly cover all three lanes. The tests allow up to 3 s of this a
  minute, and one run came close to that. These and D104 are the known times the three-lane rule (D85) gives way.
- Interceptors ram about twice a minute in the tests (D106).
- In a very small window (320×200) the BUSTED box sits over the weapon panel and a warning over the mirror's label.
  Tiny windows were already crowded before Plan 3.
- The long stress run never happens to show a formed rolling roadblock (the test pictures do), and the automatic
  test driver's way of taking gaps is only lightly checked. So your drive on the patrol ground is the main check of
  rolling roadblocks in play.
- The stress test takes about 68 minutes (D116).
- One 24 ms frame in the speed test (D116).

Coming in later plans:
- Plan 4: missions 2–6, each with its own traffic and police settings shown on the briefing screen (and with them enemy
  missiles in missions 5 and 6), the briefing screen with the boss drawing and your loadout, the mission bonus,
  respawning a short way back, and the text-size setting.
- After version 1 (design §10): police helicopters and enemy spies.

## Checks only you can do (Plan 3)

1. Drive mission 1: `dotnet run -c Release --project src/NightPursuit.App` from `C:\repos\NightPursuit`.
   - Judge the traffic density (about 6 civilian cars around you, D75).
   - Watch how civilians move aside when you come up fast behind them or fire at them (D108).
   - Civilians only pull over at Pursuit level and above, which is easiest to see on the patrol ground.
2. Start the patrol ground: from `C:\repos\NightPursuit`, run
   `dotnet run -c Release --project src/NightPursuit.App -- --mission patrol-ground`.
   It starts at heat 85 (Pursuit); one bullet on a police car (+5) takes you to Manhunt. Take your time with the
   start help: the heat and the police wait until you move off, and from then Pursuit lasts 20 s without an offence
   (D77). Check that "HEAT: PURSUIT" shows at the start, that the heat stays at 85 and the patrol cars just drive along
   as traffic until you drive off, and that no police car pops into view ahead when you do. Then:
   - get boxed in, and break out by braking, right down to a stop beside the Side car (it must not bust you, D84);
   - shake the police with smoke;
   - take the gap in a roadblock (the first one is 800 m ahead of where you move off), and try going round one on
     the shoulder (D102);
   - pass a speed trap over its limit and another under it (the first is 700 m ahead, then one every 1500 m);
   - at Manhunt, meet a rolling roadblock and take its gap (D105), and see how often the interceptors ram (D106);
   - get busted once on purpose: at Pursuit or above, after you've moved off, stop with a chasing police car within
     15 m behind or in front of you (not the Side car) and wait 3 s. After the respawn, check that "HEAT SET TO 50:
     PATROL" shows.
3. Check that everything reads without sound:
   - the heat meter, its level name and its flash when heat rises;
   - the POLICE PATROLS / POLICE PURSUIT / MANHUNT banners and their messages;
   - the BUSTED IN 3 / 2 / 1 countdown and "SPEED UP TO BREAK FREE";
   - the police warnings in the lighter blue, and the roadblock warning with its gap arrow;
   - the police light bars, with both colours, blue and red, visible at once;
   - a caught speed trap's "CAUGHT" sign and "S!" on the radar.
4. Judge whether the heat numbers (HT-2) and the police levels feel fair. You said you may retune the heat levels
   after playing. The other tuning numbers worth a look: police arriving 150–250 m behind (D100), the 20 s hold on
   each police level (D77) and the patrol ground's starting heat of 85.
5. Watch for a single hitch (the one 24 ms frame, D116), and for any stutter with heavy traffic.
6. If you ever play in a very small window, look at how crowded the HUD gets (open items).
7. Tell me whether to shorten the 68-minute stress test for later plans (D116).
