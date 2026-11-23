using NightPursuit.Core.Common;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// How a boss's cunning (1-6, from its mission file) shapes it: how soon it notices you hanging back and how fast it
/// runs. Mission 1's boss is slow to react; mission 6's reacts in a second.
/// </summary>
public static class BossCunning
{
    static readonly double[] React = [3.0, 2.5, 2.0, 1.5, 1.0, 1.0];
    static readonly double[] Pace = [56, 57, 59, 60, 62, 63];   // 125 -> 140 mph

    static int Index(int cunning) => Math.Clamp(cunning, 1, 6) - 1;

    /// <summary>Seconds you must hang back before it reacts.</summary>
    public static double ReactSeconds(int cunning) => React[Index(cunning)];
    /// <summary>Its speed when it makes a run for it, m/s.</summary>
    public static double RunPace(int cunning) => Pace[Index(cunning)];
}

/// <summary>What the boss does when you hang back (owner, 2026-11-23).</summary>
public enum BossTactic { None, Run, ShootBack, DropHazards, ReverseRam }

/// <summary>The reverse ram: brake to a stop, show the reversing lights, then back into you.</summary>
public enum RamStage { Brake, Lights, Reverse }

/// <summary>
/// The boss's own will, between its phase behaviour and the road. While it is ahead of you it never follows you below
/// 90 mph, so slowing down or stopping can't park it in your sights; above that its phases pace you as they always have.
/// Level with you or behind, its phase drives as before: there it is out of your sights, and its passing moves must be
/// free to stay slower than you rather than drive into you.
/// <para>
/// When you hang back (below 45 mph with the boss ahead) for its reaction time, it picks a tactic by its cunning: make a
/// run for it, shoot back (Gunner phase) or drop hazards (Dropper phase) with the floor lifted, or back into you. A
/// Pursuer phase that was trying to get behind you first gives up and passes (from cunning 4 it runs).
/// </para>
/// </summary>
public sealed class BossMind
{
    /// <summary>An engaged boss ahead of you never follows you below this, m/s (90 mph).</summary>
    public const double FloorSpeed = 40;
    /// <summary>Below this you are hanging back, m/s (45 mph).</summary>
    public const double HangBackSpeed = 20;
    /// <summary>Shooting back or dropping lasts at most this long, s.</summary>
    public const double WeaponSeconds = 6;
    /// <summary>A run ends once you are back within this, m, and no longer hanging back.</summary>
    public const double RunEndsWithin = 200;
    /// <summary>A run is held this far under your car's top speed, m/s, so driving flat out always catches it.</summary>
    public const double RunBelowYourTop = 6;
    /// <summary>Your armor at or below which you count as badly hurt (cunning 4 and up press the attack).</summary>
    public const double YouBadlyHurt = 35;
    /// <summary>The ram is only chosen within this far ahead of you, m, and above this much health.</summary>
    public const double RamWithin = 80, RamAboveHealth = 0.4;
    /// <summary>The ram's hard stop (m/s^2), its reversing lights (s), its top speed backwards (m/s).</summary>
    public const double RamBrake = 25, LightsSeconds = 1.0, ReverseSpeed = 11;
    /// <summary>A ram that has not touched you in this long reversing (s), or with you this far behind (m), gives up and runs.</summary>
    public const double ReverseSeconds = 5, RamGivesUpBeyond = 120;
    /// <summary>What the ram costs you (armor points) and the boss (share of its full health).</summary>
    public const double RamHurtsYou = 10, RamHurtsBoss = 0.04;

    public BossTactic Tactic { get; private set; }
    public RamStage RamStage { get; private set; }
    /// <summary>How long you have been hanging back, s (0 while you aren't, or while a tactic runs).</summary>
    public double HangingBack { get; private set; }
    /// <summary>Seconds into the current tactic (or ram stage).</summary>
    public double TacticTime { get; private set; }
    /// <summary>The last tactic that did you no harm (cunning 6 won't try it twice in a row).</summary>
    public BossTactic LastFailed { get; private set; }
    /// <summary>True while backing into you.</summary>
    public bool Reversing => Tactic == BossTactic.ReverseRam && RamStage == RamStage.Reverse;
    /// <summary>True while its reversing lights show (the second before it moves backwards, and while it does).</summary>
    public bool ReversingLights => Tactic == BossTactic.ReverseRam && RamStage is RamStage.Lights or RamStage.Reverse;

    double _yourArmorAtStart;
    BossTactic? _started;
    bool _reversingCue;

    /// <summary>The tactic started since the last call, for the world to announce (each is announced once).</summary>
    public BossTactic? TakeStarted()
    {
        var t = _started;
        _started = null;
        return t;
    }

    /// <summary>True once when the reversing lights come on.</summary>
    public bool TakeReversingCue()
    {
        bool cue = _reversingCue;
        _reversingCue = false;
        return cue;
    }

    /// <summary>
    /// The tactic for the moment, by cunning (pure, for the table in the design):
    /// 1 always runs; 2 its phase's weapon or a run, at random; 3 adds the ram (only when it is close and healthy), at
    /// random; 4 and up pick what fits: hurt, it runs; you badly hurt, it shoots back or rams; else its weapon, else a
    /// ram, else a run. 6 never picks the tactic that just failed if it has another.
    /// </summary>
    /// <param name="weapon">ShootBack in a Gunner phase, DropHazards in a Dropper phase with a drop ready, else None.</param>
    public static BossTactic Pick(int cunning, BossTactic weapon, bool canRam, bool hurt, bool youBadlyHurt, BossTactic lastFailed, IRng rng)
    {
        if (cunning <= 1) return BossTactic.Run;
        if (cunning == 2) return weapon != BossTactic.None && rng.Next(2) == 0 ? weapon : BossTactic.Run;
        if (cunning == 3)
        {
            var options = new List<BossTactic> { BossTactic.Run };
            if (weapon != BossTactic.None) options.Add(weapon);
            if (canRam && !hurt) options.Add(BossTactic.ReverseRam);
            return options[rng.Next(options.Count)];
        }

        if (hurt) return BossTactic.Run;
        var order = new List<BossTactic>();
        if (youBadlyHurt)
        {
            if (weapon == BossTactic.ShootBack) order.Add(weapon);
            if (canRam) order.Add(BossTactic.ReverseRam);
        }
        if (weapon != BossTactic.None) order.Add(weapon);
        if (canRam) order.Add(BossTactic.ReverseRam);
        order.Add(BossTactic.Run);
        if (cunning >= 6 && lastFailed != BossTactic.Run)
            return order.FirstOrDefault(t => t != lastFailed, BossTactic.Run);
        return order[0];
    }

    /// <summary>
    /// The boss's drive for this step: the phase's intent held at the floor, or replaced by the tactic it is running.
    /// Called only while it is engaged and driving.
    /// </summary>
    public DriveIntent Apply(BossCar b, WorldView view, DriveIntent phase, double dt)
    {
        var p = view.Player;
        double dD = b.Distance - p.Distance;
        bool hanging = dD > 0 && p.Speed < HangBackSpeed;

        if (Tactic != BossTactic.None && Step(b, view, phase, dD, hanging, dt) is { } running) return running;

        HangingBack = hanging ? HangingBack + dt : 0;
        if (Tactic == BossTactic.None && hanging && HangingBack >= BossCunning.ReactSeconds(b.Definition.Cunning) - 1e-9)
        {
            React(b, view, dD);
            if (Tactic != BossTactic.None && Step(b, view, phase, dD, hanging, 0) is { } started) return started;
        }
        return Floor(b, view, phase);
    }

    /// <summary>The boss backed into you: the ram worked, and it takes off.</summary>
    public void BackedIntoYou()
    {
        if (LastFailed == BossTactic.ReverseRam) LastFailed = BossTactic.None;
        Start(BossTactic.Run, yourArmor: _yourArmorAtStart);
    }

    /// <summary>Forgets everything (a respawn puts the boss back to ease in again).</summary>
    public void Reset()
    {
        Tactic = BossTactic.None;
        RamStage = RamStage.Brake;
        HangingBack = 0;
        TacticTime = 0;
        LastFailed = BossTactic.None;
        _started = null;
        _reversingCue = false;
    }

    static DriveIntent Floor(BossCar b, WorldView view, DriveIntent phase)
    {
        if (b.Distance <= view.Player.Distance) return phase;
        double floor = b.DriverState is BossPursuitDriver.State { Phase: BossPursuitPhase.DropBack }
            ? FloorSpeed - BossPursuitDriver.DropBackSlower   // so it can still get behind you at 90 mph
            : FloorSpeed;
        return phase with { TargetSpeed = Math.Max(phase.TargetSpeed, floor) };
    }

    void React(BossCar b, WorldView view, double dD)
    {
        var p = view.Player;
        int cunning = b.Definition.Cunning;
        // A Pursuer trying to get behind you, kept from it: it gives up and passes (its phase carries on), or runs from cunning 4.
        if (b.Phase.Behaviour == AgentKind.Pursuer
            && b.DriverState is BossPursuitDriver.State { Phase: BossPursuitPhase.DropBack or BossPursuitPhase.Attack } chase)
        {
            if (cunning >= 4) Start(BossTactic.Run, p.Armor);
            else
            {
                chase.Phase = BossPursuitPhase.Overtake;
                chase.Target = DriverHelpers.PassingLane(b, p).Lateral;
                HangingBack = 0;
            }
            return;
        }

        var weapon = b.Phase.Behaviour switch
        {
            AgentKind.Gunner => BossTactic.ShootBack,
            AgentKind.Dropper when DropperDriver.NextDrop(b.Arms) is not null => BossTactic.DropHazards,
            _ => BossTactic.None,
        };
        bool hurt = b.HealthFraction <= RamAboveHealth;
        bool canRam = cunning >= 3 && !hurt && b.Shield <= 0 && dD <= RamWithin;
        Start(Pick(cunning, weapon, canRam, hurt, p.Armor <= YouBadlyHurt, LastFailed, view.Rng), p.Armor);
    }

    void Start(BossTactic t, double yourArmor)
    {
        Tactic = t;
        TacticTime = 0;
        RamStage = RamStage.Brake;
        HangingBack = 0;
        _yourArmorAtStart = yourArmor;
        _started = t;
    }

    /// <summary>Ends the current tactic; one that did you no harm is remembered as failed.</summary>
    void End(double yourArmor, bool chainRun)
    {
        if (Tactic != BossTactic.Run && yourArmor >= _yourArmorAtStart) LastFailed = Tactic;
        else if (Tactic != BossTactic.Run && LastFailed == Tactic) LastFailed = BossTactic.None;
        Tactic = BossTactic.None;
        TacticTime = 0;
        HangingBack = 0;
        if (chainRun) Start(BossTactic.Run, yourArmor);
    }

    double RunSpeed(BossCar b, WorldView view) =>
        Math.Min(BossCunning.RunPace(b.Definition.Cunning), view.PlayerTopSpeed - RunBelowYourTop);

    /// <summary>One step of the running tactic; null once it has ended (the phase and floor take over).</summary>
    DriveIntent? Step(BossCar b, WorldView view, DriveIntent phase, double dD, bool hanging, double dt)
    {
        var p = view.Player;
        TacticTime += dt;
        switch (Tactic)
        {
            case BossTactic.Run:
                if (dD <= 0 || (!hanging && dD <= RunEndsWithin)) { End(p.Armor, chainRun: false); return null; }
                return phase with { TargetSpeed = RunSpeed(b, view) };

            case BossTactic.ShootBack:
            case BossTactic.DropHazards:
                bool samePhase = b.Phase.Behaviour == (Tactic == BossTactic.ShootBack ? AgentKind.Gunner : AgentKind.Dropper);
                if (!samePhase || !hanging || TacticTime >= WeaponSeconds)
                {
                    bool chain = samePhase && hanging && b.Definition.Cunning >= 5;
                    End(p.Armor, chain);
                    return Tactic == BossTactic.Run ? phase with { TargetSpeed = RunSpeed(b, view) } : null;
                }
                return phase;   // its phase drives at its own pace, under the floor if need be

            case BossTactic.ReverseRam:
                if (dD <= 0) { End(p.Armor, chainRun: false); return null; }                  // you got past it
                if (b.HealthFraction <= RamAboveHealth || b.Shield > 0 || dD > RamGivesUpBeyond
                    || (RamStage == RamStage.Reverse && TacticTime >= ReverseSeconds))
                {
                    End(p.Armor, chainRun: true);                                             // it gives up and takes off
                    return phase with { TargetSpeed = RunSpeed(b, view) };
                }
                return Ram(b, p);
        }
        return null;
    }

    DriveIntent Ram(BossCar b, PlayerCar p)
    {
        switch (RamStage)
        {
            case RamStage.Brake:
                if (b.Speed <= 0.5)
                {
                    RamStage = RamStage.Lights;
                    TacticTime = 0;
                    _reversingCue = true;
                }
                return new DriveIntent(0, b.Lateral, BrakeRate: RamBrake);
            case RamStage.Lights:
                if (TacticTime >= LightsSeconds - 1e-9)
                {
                    RamStage = RamStage.Reverse;
                    TacticTime = 0;
                }
                return new DriveIntent(0, b.Lateral, BrakeRate: RamBrake);
            default:
                return new DriveIntent(-ReverseSpeed, p.Lateral);
        }
    }
}
