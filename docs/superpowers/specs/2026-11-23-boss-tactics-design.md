# Boss tactics, part 1: its own pace, escaping, and what it does when you slow down (design)

**Date:** 2026-11-23 · **Size:** a project, part 1 of 3 (see "Later") · **Owner's words:** "When chasing the bosses, if I
slow down, they slow down … if I stop or drive slowly, I can just hammer the boss with my machine guns. What's the boss's
motivation to slow down when I do?" Then, after watching real chases: "the boss's reason and tactic depends on you."

## Intent and why

Today almost every boss phase sets its speed from yours: the Runner keeps about 70 m ahead at your speed ±22 mph, the
Gunner 45 m, the Dropper a gap scaled to your speed, the Blocker brake-checks at your speed − 11 mph, the Pursuer phase
holds a gap off your speed, and a boss 600 m ahead waits at your speed − 34 mph. So if you stop, the boss stops 45–70 m
in front of you, inside your guns' 380 m reach, and you can hammer it. Nothing in the story explains it; the rules
were written only to keep the boss on screen. It also makes the fight less fun.

The owner wants the boss to behave like a driver with its own goals: it drives at its own pace, it can get away, and
when you slow down a lot it chooses a tactic that fits the moment. Bosses get smarter mission by mission.

## Goals

1. **A speed floor.** An engaged boss never follows you below 90 mph. Above that its phases fight as they always have
   (they still pace you, hold their gaps, come alongside, brake-check): at those speeds you are driving, and that is the
   fight. Below 90 mph it no longer follows you down, so slowing down or stopping can't park it in your sights. Only the
   tactics below, caltrops, oil and spins take it lower.
2. **It can get away.** A boss far enough ahead for long enough escapes. The mission fails and you lose a spare car.
3. **Tactics when you slow down a lot.** The boss picks one of: make a run for it; slow down with you and shoot back
   from in front; drop oil or caltrops; back into you, damaging both cars, then take off; or drop behind you (only if you
   let it). Its choice depends on the moment: its phase's weapons, its health, yours, and where you both are.
4. **Smarter mission by mission.** Mission 1's boss is a thug with one idea; mission 6's plans: it reacts sooner, knows
   more tactics, picks the one that fits and chains them.
5. **Everything important is on screen** (owner is hard of hearing): every tactic has a visible cue.

## Non-goals (this part)

- Damage changing how the boss drives (part 2), and the bigger schemes: reinforcements, a trap ahead, marking you for
  the police (part 3).
- Damage changing how *your* car drives (owner, 2026-11-23: wanted, but later).
- No change to the boss phases, their health thresholds, banners and hints, or the finale.
- Ramming the boss forward still never hurts it (call 19). Only its own reverse ram hurts it.

## Anti-goals (failure even if it works)

- A boss that can again be parked in your sights by driving slowly or stopping.
- An escape you could not have prevented: a boss that is faster than your top speed while running.
- A tactic with no visible warning, or one that depends on sound.
- A boss that wrecks itself by accident and hands you a cheap win (it only reverse-rams while healthy enough to survive).
- Tactics that make the early missions much harder: mission 1 stays gentle.

## Approaches (owner picked 1)

1. **A tactics layer over the phases (chosen).** A small "boss mind" above today's phase behaviours watches whether you
   are hanging back, where you are and how hurt each car is. Normally the phase drives, at the boss's pace; when you
   slow down a lot, the mind takes over for a tactic, then hands back. Phases stay as they are; each tactic is testable
   on its own; the choice can depend on the phase's weapons.
2. Each phase gets its own reactions. The same logic six times; the phases drift apart. Rejected.
3. The boss picks its phase freely. Throws away the phase design, banners, hints and mission files. Rejected.

## The design

### Speeds (mph as the HUD shows them; m/s in brackets)

- **Your top speed:** 155 mph (69).
- **Floor:** 90 mph (40). Every phase move that sets the boss's speed from yours (the Runner's, Gunner's, Dropper's and
  Pursuer's held gaps, the Pursuer's attack and drop-back, the Armored back-off, the Blocker's brake-check) is held at
  or above it. Below it only the "slow down and shoot" and "reverse ram" tactics, caltrops, oil and spins.
- **Its pace when it runs:** 125–140 mph (56–63) by cunning (below). Below your top speed, so a player who drives flat
  out always closes in.
- The "far behind, catch up at +25 m/s" rule stays. The "far ahead, wait for you" rule (600 m) goes: getting away is now
  the point.

### Getting away

- When the boss is more than **600 m** ahead (just beyond your guns' reach), a large flashing banner reads
  **BOSS GETTING AWAY** with the distance and a countdown.
- It **escapes** if it stays beyond 600 m for **20 seconds**, or reaches **1,200 m** (out of sight).
- Check of the numbers: from a dead stop with the boss 70 m ahead, the gap peaks at about 430 m (12 s to get back up to
  speed), then you close at about 20 mph. So a short stop never sets off the warning; only dawdling does.
- **When it escapes:** "MISSION FAILED: THE BOSS ESCAPED", and the mission starts again from its briefing. **You keep
  the points you earned** during the failed attempt, and the spare cars you have at that moment, then **one spare car is
  taken off** (owner, 2026-11-23: "the cost of a car is probably high enough"). If you have no spare car left when it
  escapes, it is game over, and the normal game-over and high-score screens follow with your score as it stands. Same
  rule in practice (where scores are never recorded).
- Why keeping the points can't be farmed: an escape costs a car, and a failed attempt earns only the cars you destroyed
  (no boss, no mission bonus), a few thousand points, far short of the 25,000 an extra car takes (below). So letting the
  boss escape always costs more cars than it earns.

### Extra cars (owner, 2026-11-23)

- You earn an **extra car every 25,000 points**: at 25,000, 50,000, 75,000 and so on, counted on your total score, so
  points carried from earlier missions count. With only six missions, a good player should earn a few in a full game.
- Points from anywhere count: destroying cars, shooting down missiles, and the bonus for finishing a mission.
- **At most 5 spare cars.** A car earned while you already have 5 is not kept.
- **On screen:** "EXTRA CAR" in the message line when it is earned during play, and on the results screen when the
  mission bonus earns it; the spare-car icons go up by one. Nothing depends on sound.
- Same rule in practice.
- The countdown stops while you are respawning, while paused, and during the finale.

### "Hanging back" and the tactics

- **You are hanging back** when you have driven below **45 mph (20)** for a while (the while depends on the boss's
  cunning, below) while the boss is engaged and ahead of you.
- When that happens the boss mind picks a tactic, holds it until it is done (or you speed up and pass it), then hands
  back to the phase.
- **The tactics**, each with its on-screen cue:
  1. **Make a run for it.** Speeds up to its running pace. Cue: "BOSS MAKING A RUN FOR IT". The escape rules above apply.
  2. **Slow down and shoot back.** Only in a phase with rear guns (Gunner). It matches your speed for a few seconds and
     fires at you from in front. Cue: the existing gunner warnings plus "BOSS SHOOTING BACK".
  3. **Drop hazards.** Only in a phase with chutes (Dropper). It drops oil or caltrops in your lane. Cue: the existing
     drop warnings.
  4. **Back into you.** Any phase, only while the boss is healthy enough to survive it (see below). It brakes hard and
     reverses into you, then takes off at 140 mph (a run). Cue: "BOSS REVERSING" and its reversing lights (a new detail
     line on the boss outline) for about 1 s before it moves backwards.
  5. **Drop behind you.** This is the Pursuer phase's existing move (it drops back and attacks from behind), with one
     change: if you slow down to keep it from getting behind you (you hang back while it is beside you or just behind),
     it gives up, passes you and goes back to its phase, or, from cunning 4, makes a run for it. Not a new move for the
     other phases.

### The reverse ram

- A new ability: a car can drive backwards (today speeds stop at 0). Only the boss uses it, only in this tactic, at up to
  about 25 mph (11) backwards.
- **Only up close.** The boss chooses the ram only when it is within 80 m ahead of you; farther away it runs or uses its
  phase's weapon instead. It brakes (below the floor), shows its reversing lights, then backs into you.
- **It hurts both cars.** You: 10 armor points, with the message "THE BOSS BACKED INTO YOU". The boss: 4% of its full
  health. It is the boss's fault: no heat for you, and none of the usual "you rear-ended it" rules apply (today a contact
  from behind blames you, pushes the boss forward and never hurts it; the reverse ram gets its own path).
- **The boss only tries it** while above 40% of its full health and not shielded, so a single ram can never destroy it.
  If something else finishes it during the ram, the finale starts from whatever speed it has (a backwards speed counts
  as 0; the finale already starts at least as fast as you).

### Cunning: smarter mission by mission

Each boss has a **cunning** level 1–6, from its mission file's boss block (`"cunning": 3`); missions 1–6 get 1–6, the
practice grounds 1. It sets:

| Cunning | Reacts after hanging back for | Tactics it knows | How it chooses |
|---|---|---|---|
| 1 | 3.0 s | run | always runs |
| 2 | 2.5 s | run, its phase's weapon (shoot back or drop) | at random among those |
| 3 | 2.0 s | + reverse ram | random, but never rams when hurt |
| 4 | 1.5 s | all | picks what fits: hurt → run; you badly hurt → shoot back or ram; else its weapon |
| 5 | 1.0 s | all | as 4, and chains: ram then run |
| 6 | 1.0 s | all | as 5, and never repeats a tactic that just failed (you dodged the drop, out-ran the ram) |

Also by cunning: its running pace, from 125 mph at 1 to 140 mph at 6.

### What stays

The phases (their order, health thresholds, banners, hints), the caltrops slow, oil spins, the boss bar, the finale,
and the leash that brings a far-behind boss back.

## Builder's calls (mine, for the owner to check)

- The numbers: floor 90 mph, running pace 125–140 mph, hanging back below 45 mph, escape at 600 m for 20 s or
  1,200 m, the reaction times and the cunning table. Cost if wrong: tuning.
- The reverse ram: only within 80 m, 10 points to you and 4% to the boss, only above 40% health, up to 25 mph
  backwards, reversing lights 1 s before.
- Above 90 mph the boss still paces you as now; the floor, not a higher pace, is what ends the exploit (that was option
  A as you approved it). A reviewer found that a higher pace would stop the Pursuer, Gunner, Dropper and Blocker phases
  working at all unless you drove at 125+ mph.
- After an escape ends the game, your score as it stands can make the high-score table.
- "Shoot back" only with rear guns and "drop hazards" only with chutes, so each phase stays itself.
- Practice grounds at cunning 1.
- At most 5 spare cars (my number): enough to reward good play, few enough that the HUD's car icons still fit and a
  long game keeps some danger.
- (Settled by the owner, 2026-11-23.) An escape keeps your points and takes one car; there is no reset to the mission's
  start. Cars lost during the failed attempt stay lost too.

## Left to the builder

The exact on-screen layout of the banners, the reversing-light detail in the line-art style, how a tactic ends and
hands back, and the tuning within the numbers above, checked by tests and snapshots.

## Later (not this part)

- **Part 2:** damage changes how the boss drives (slower, less steady), and its tactic choices take it into account.
- **Part 3:** bigger schemes: reinforcements while you dawdle, a trap ahead, marking you for the police.
- **Your own car:** damage makes it slower and less nimble (owner, 2026-11-23).

## How it is checked

- Core tests: a stopped or slow player no longer holds an engaged boss in gun range; the escape warning, countdown,
  escape and its cost; the countdown pauses; each tactic's trigger and cue by cunning level; the reverse ram's damage
  to both and that it never destroys a boss on its own; the finale from a reversing boss; a full chase at top speed
  always closes the gap.
- Snapshots of each new banner and of the reversing boss.
- `--stress` still plays every mission through.
