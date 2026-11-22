# Boss finale: the boss crashes off the road and explodes (design)

**Date:** 2026-11-22 · **Size:** a medium piece of work (one plan) · **Owner's words:** "The boss cars should spin
forward off the road and explode … have that animation on the screen before we switch to the mission post-mortem
screen." Then: spin out, then flip and explode, "a single bright flash on the screen with the explosion", "remember the
art style choice", "make it different every time. Vary the direction, sometimes spin, sometimes flip", and "forget the
slow motion part. Let's have it be full speed."

## Intent and why

Beating a boss is the high point of each mission, but today you never see it happen. The moment the boss is wrecked
its car vanishes, the usual three staggered explosions start, and on that same step the run is Completed, so the
results screen covers the road and freezes it. The owner wants a short, full-speed crash to watch first: the boss's
car spins out, leaves the road, rolls or flips, and blows up with one bright flash. Then the results screen comes up.
It should look different from one boss to the next.

## Goals

1. **A finale after every boss kill.** When the boss is destroyed, the run does not end at once. A short finale plays
   (about 4 s), then the run is Completed exactly as now (same score, same bonus, same results screen).
2. **Three kinds of crash, left or right, never the same twice in a row:**
   - **Spin-off:** a flat spin (like today's spin-out) that slides off the road onto the verge, where the car stops
     and explodes.
   - **Roll:** spins out, trips at the road edge and rolls sideways over its long axis, 1 or 2 full turns, then
     explodes.
   - **Flip:** spins out, trips at the road edge and flips end over end, 1 or 2 full turns, then explodes.
   - It leaves by the left or right edge. The number of turns (1 or 2) for a roll or flip is picked too.
3. **One bright flash** on the whole screen at the moment of the explosion, with the existing boss kill bursts
   (three staggered explosions) and a burning wreck left on the verge.
4. **Full speed.** The world runs at normal speed throughout. (Slow motion was proposed and dropped by the owner.)
5. **Always in view.** The crash happens ahead of you, on screen, wherever the boss was when it died (ahead, beside
   or behind).
6. **Safe.** From the boss kill to the results, nothing can hurt you or change the outcome: no damage, no wreck, no
   bust, no heat change, no score change.
7. **Skippable.** Space, Enter or pad A goes straight to the results.
8. **The line-art look (owner's standing choice, 2026-11-20 line-art design):** Combined style only. Glow with New
   Wave breaks, presenting edges only (black fills hide what is behind), heavy outline and light detail pens, depth
   fade, the boss's own white colour and its look (Fin, Twin, Dome) kept recognisable while it tumbles.

## Non-goals

- No slow motion (owner).
- No finale when the player is wrecked or busted, and none for ordinary enemies.
- No change to scoring, bonuses, mission rules, or how the boss is fought.
- No new setting. (The flash is single and short; see the builder's calls.)

## Anti-goals (failure even if it works)

- **The extra effects the owner rejected** on 2026-11-20: no double images, shaking "breakdown" views, dizzy circling,
  smeared trails. One flash, the bursts, and the car's motion. Nothing else.
- **A strobe.** Exactly one flash per finale.
- **Losing the run after the boss is dead.** No traffic, police, enemy or leftover missile may wreck, bust or damage
  you during the finale.
- **The same crash every time**, including after a restart of the same mission.
- **A crash you cannot see:** off screen, behind you, hidden behind a hill, or covered by the HUD.
- **Important information only in sound.** The finale is fully visual; sound only adds to it.

## Approaches (owner picked 1)

1. **Core runs the finale (chosen).** Beating the boss puts the run in a new finale state. Core moves the boss's car
   through the chosen crash, keeps the player safe, and ends the run as Completed when it is over. The App only draws
   what Core says: the car's roll, pitch or spin, the bursts and the flash. Testable in Core (timing, safety, the
   choice of crash, determinism), and the snapshot tool can draw any moment of any crash on demand.
2. A short film played by the App alone after the run ends. Fewer rule changes, but the road is frozen behind
   screens, so the App would need its own moving copy of the world. Hard to test, and the crash could happen out of
   view. Rejected.
3. Real tumbling physics. Most natural, but hard to keep in view and far more work than 4 s of animation needs.
   Rejected.

## The design

### Run states and timing

- `RunState` gains **`Finale`**, between `Playing` and `Completed`. When the boss is destroyed (the director's
  completion test sees the boss Wrecked), the run enters `Finale` instead of `Completed`. A mission without a boss
  still completes at once, as now.
- The finale has three parts (times are targets for the builder to tune by eye, total about 4 s):
  1. **Spin-out** (about 1.2 s): the car spins flat and slides toward the chosen edge, keeping much of its forward
     speed.
  2. **Off the road** (about 1.3 s): for a roll or flip, the car trips at the road edge and turns 1 or 2 times while
     it carries on forward and outward, landing on the verge. For a spin-off it keeps spinning, slows and stops on the
     verge.
  3. **Explosion and hold** (about 1.5 s): it explodes (the flash, the three bursts) and a burning wreck stays on
     the verge. When the hold ends, the run becomes `Completed`.
- The world keeps running at normal speed during the finale: traffic, police and other enemies carry on.
- **Pause** works in the finale (P, and losing focus), as in play. In practice, R restarts from that pause as it does
  from any pause; R alone does nothing while the finale runs. After the finale, R on the results works as now.
- **Skip:** Space, Enter or pad A during the finale ends it at once: the explosion is skipped and the run becomes
  `Completed`. A key held down from the fight (Space is fire) must not skip it by itself; only a new press counts.
- If the player's car is wrecked or busted in the same step the boss dies, the boss kill still wins, as today (the
  mission is complete and no spare car is lost). The car's armor stays at 0, so the bonus is today's, and it must not
  be wrecked again by the "armor at 0" rule while the finale runs. No "BUSTED" line or banner is shown, since no car
  is lost.
- If the boss dies while the player's car is wrecked and waiting to respawn (a missile or strike already in flight),
  the respawn happens at once (the spare car is used, as today, where the kill is only noticed after the respawn),
  and the finale starts from the new car. The outcome is today's; only the wait is shorter.

### The boss's car during the finale (what the code needs)

- Today `World.Destroy` marks the boss Wrecked; a gunfire, missile or strike kill also drops a sliding `Wreck` on the
  road and removes the car. Every renderer, the mirror and targeting skip Wrecked cars.
- For the boss, Destroy still scores it and says "NAME DESTROYED +points", but the car is kept in a **separate finale
  state** (not Wrecked), with no on-road `Wreck`. Targeting, collisions, police and the boss bar treat it as gone; the
  rule that keeps the boss on the road does not apply to it. The radar marks it as destroyed.
- The explosion is a **new Core event** at the explosion moment. The App's boss bursts, the flash and the boss
  explosion sound start from that event instead of from the kill. When it explodes, the car is removed and the
  burning `Wreck` is placed on the verge.

### Keeping the player safe and the crash in view

- At the kill, enemy missiles and hostile bullets still in flight are cleared (as a respawn does).
- During the finale the player's car **drives itself**: steering and weapons are ignored, it holds its lane and eases
  its speed so the crash stays ahead, ideally 40 to 150 m away. A boss killed beside or behind you keeps more speed
  than your car and passes you, so it is ahead before it leaves the road.
- The player's car takes no damage and cannot be wrecked or busted; heat does not change; nothing scores.
- **Other cars and the road furniture:** no new traffic, police, roadblocks or speed traps appear during the finale.
  What is already there carries on, but nothing touches the player's car (it passes through) and the tumbling boss
  touches nothing (no civilian or police kills, so no heat or score messages). The crash ends off the road, on the
  verge, where traffic does not drive, so a car between you and the boss can hide the crash only briefly.
- Boss points and the "NAME DESTROYED +points" message come at the kill, as now. The mission bonus and the score
  carried to the results are worked out when the finale ends; because nothing scores during the finale, they are the
  same as today's.

### Choosing the crash ("different every time")

- Each mission's random numbers come from a fixed seed, so a restart replays the same traffic. The crash must not come
  from those numbers, or a mission would always end the same way.
- The App makes one non-repeatable random source per launch (seeded from the clock) and the `Game` takes the next
  crash from it at each boss kill. Core itself stays deterministic: given the same choice, the finale plays out the
  same way, so tests and the `--stress` run can name a crash and get exactly it.
- **Never the same kind and side twice in a row** within one game (including after restarts and across missions).
  With 3 kinds × 2 sides there are always 5 other choices; the number of turns is picked freely.

### Drawing (App)

All in the Combined line-art pens, black-filled, depth-faded, with the boss's colour and look:

- **Spin-out and spin-off:** the existing spin drawing (lean and narrowing with the spin angle).
- **Roll:** seen from behind, a car rolling over its long axis keeps its rear face toward you, so the rear outline
  (body, fins or dome, tyres) turns about its centre through the roll angle, and rises a little from the ground while it
  turns.
- **Flip:** a car turning end over end shows, in turn, its rear face, its underside, its front face (upside down), its
  roof, and its rear again. Each presenting face is drawn as a flat outline whose height follows the turn (full when
  square on, thin when edge on), black-filled so the hidden faces never show. New outlines are needed for the
  underside (a plain panel with the two axles and four tyre ends) and the roof (with a windscreen line, and the boss's
  fins, twin fins or dome as a simple shape so the look stays recognisable). The front face may reuse the rear
  outline turned upside down.
- **Off the road:** the car is drawn beyond the road edge on the verge, among the posts and lamps, clipped by hills
  like any car.
- **The flash:** one full-screen white flash at the explosion: it appears at once and fades out in about 0.25 s. It
  sits under the HUD text, so no readout is hidden. It is not repeated.
- **Explosion:** the existing boss kill bursts (`SpawnBossKill`), moved from the kill to the explosion moment, then a
  burning wreck on the verge (the existing wreck and flame drawing). A finale that is skipped shows no explosion.
- **Text:** while the finale runs, a small line "SPACE OR ENTER: SKIP" in the HUD's usual hint style. The boss bar
  disappears at the kill, as now.
- **Mirror and radar:** behave as for any car; the radar marks the boss as destroyed from the kill.

### Sound

Nothing important is only heard. The existing spin sound plays at the start of the spin-out, the boss explosion
sound moves from the kill to the explosion moment, and the music changes to the results music when the results
screen appears, as now. A skipped finale plays no explosion sound.

## Builder's calls (mine, for the owner to check)

- **The flash peaks at about 60% white**, not pure white, and lasts about 0.25 s. One flash is well under the
  three-flashes-a-second limit that matters for people sensitive to flashing light, so it is not tied to a setting.
  Cost if wrong: a setting to turn it off.
- **About 4 s in all.** Long enough to watch, short enough not to drag after every boss. Cost if wrong: tuning
  numbers.
- **The player's car drives itself during the finale.** It keeps the crash in view and makes "nothing can hurt you"
  simple. Cost if wrong: the player might like to keep steering; that would need the crash to follow the player's view.
- **Equal odds** for the three kinds and two sides, never the same kind and side twice in a row; 1 or 2 turns.
- **A skip key**, so a player replaying a mission is never held up.
- **"Different every time" holds within one game.** Two launches of the game can start with the same crash.
- **Nothing touches you in the finale, and no new traffic or police appear.** The player's car passes through what is
  already there, rather than steering round it. Cost if wrong: a car seen passing through another for a moment.
- **A boss killed during the respawn wait** gets its finale after an immediate respawn, with the spare car used as
  today.
- **Pause, then R** restarts a practice mission during the finale, as from any pause.

## Left to the builder

Exact timings within the targets, the spin and turn rates, how high a roll or flip lifts the car, the paths across
the verge, the autopilot's easing, the underside and roof outlines for each boss look, and the flash's exact fade.
Each is checked by eye with new snapshot scenes (any kind and side, at chosen moments of the finale) at 1280×720 and
960×540.

## How it is checked

- Core tests: the run enters Finale at a boss kill and Completed when it ends; the bonus and score match today's;
  nothing can damage, wreck or bust the player or change heat or score during the finale; the skip ends it; pause
  holds it; a named crash plays the same way twice; the picker never repeats kind and side twice in a row; a
  mission with no boss completes at once.
- Snapshot scenes for each kind and side at several moments, including the flash, sent to the owner as a sheet.
- `--stress` still plays every mission through, and `--perf` stays within budget.
