using NightPursuit.Core.Drivers;
using NightPursuit.Core.Police;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Simulation;

public sealed partial class World
{
    /// <summary>A heat banner does not replace one that went up less than this long ago (the message still goes out).</summary>
    const double BannerHoldSeconds = 1.0;

    /// <summary>How much the police want the player; stays 0 in a mission without police.</summary>
    public HeatMeter Heat { get; } = new();

    /// <summary>Adds heat for <paramref name="reason"/>, publishing the change and, if the level moved, its banner and message.</summary>
    public void AddHeat(double amount, string reason)
    {
        var from = Heat.Level;
        var change = Heat.Add(amount);
        if (amount != 0) Publish(new HeatChanged(Time, amount, Heat.Value, reason));
        if (change is { } to) PublishLevelChange(from, to);
    }

    /// <summary>
    /// True from the start of a mission with a police section until the first step the player is driving faster than
    /// 5 mph (<see cref="BustedWatch.SpeedLimit"/>). Until then the heat does not die down (nor does the time in its
    /// level count), and the police dispatcher only places the starting patrol cars on the first step
    /// (<see cref="PoliceDispatcher.SeedWhileHeld"/>): nothing chases, spawns or is placed while the start help is up.
    /// A respawn does not set it again. Offences still add heat.
    /// </summary>
    public bool HeatHeldUntilMoved { get; set; }

    /// <summary>
    /// A mission with police starts at <paramref name="startingHeat"/>, held until the player moves
    /// (<see cref="HeatHeldUntilMoved"/>). Starting at Patrol or above says so once: "HEAT: PURSUIT", for example.
    /// </summary>
    public void StartPolice(double startingHeat)
    {
        Heat.Set(startingHeat);
        HeatHeldUntilMoved = true;
        if (Heat.Level >= HeatLevel.Patrol) Publish(new Message(Time, $"HEAT: {HeatRules.LevelName(Heat.Level)}", Tone.Bad));
    }

    /// <summary>The countdown to being busted; runs in StepOnce after the heat update.</summary>
    public BustedWatch Busted { get; } = new();
    /// <summary>False until the player has moved above 5 mph since the mission start or the last respawn (F11); the watch counts nothing before.</summary>
    public bool BustedArmed { get; set; }
    /// <summary>True from a bust until the respawn, which then sets the heat to 50.</summary>
    public bool BustedPending { get; set; }

    /// <summary>
    /// Sets the heat outright (no decay rules; unlike <see cref="AddHeat"/> it does not reset the quiet-spell timer), publishing the change
    /// (nothing when the value is the same) and, if the level moved, the level event. The caller says why on screen.
    /// </summary>
    public void SetHeat(double value, string reason)
    {
        double before = Heat.Value;
        var from = Heat.Level;
        var change = Heat.Set(value);
        if (Heat.Value != before) Publish(new HeatChanged(Time, Heat.Value - before, Heat.Value, reason));
        if (change is { } to) Publish(new HeatLevelChanged(Time, from, to));
    }

    /// <summary>The police have held you: the car is lost like a wreck, and the respawn sets the heat to 50.</summary>
    public void BustPlayer()
    {
        Player.Armor = 0;
        Player.State = VehicleState.Wrecked;
        Player.StateTimer = 3;
        Busted.Reset();
        BustedPending = true;
        Publish(new PlayerBusted(Time));
        Publish(new PlayerWrecked(Time, "BUSTED"));
        Publish(new Message(Time, "BUSTED - YOU LOSE A SPARE CAR", Tone.Bad));
        Announce("BUSTED", "YOU LOSE A SPARE CAR", 3, urgent: true);
    }

    double _lastPoliceShotMessage = double.NegativeInfinity;

    /// <summary>Marks <paramref name="a"/> as hurt by the player: for the next <see cref="HeatRules.BlameSeconds"/> its destruction counts against you.</summary>
    public void BlamePlayer(AgentCar a)
    {
        if (a.Faction != Faction.Hostile) a.PlayerBlameUntil = Time + HeatRules.BlameSeconds;   // hostiles are never blamed, so their hash terms stay constant
    }

    /// <summary>
    /// The player hit <paramref name="a"/> (a bullet, or a contact that was the player's fault): heat for a civilian or police car, nothing for an enemy.
    /// Civilians and police never change the score.
    /// </summary>
    public void OffenceHit(AgentCar a, bool bullet)
    {
        switch (a.Faction)
        {
            case Faction.Civilian when a.HitHeatCooldown <= 0:
                a.HitHeatCooldown = HeatRules.HitCooldown;
                AddHeat(HeatRules.CivilianHit, "CIVILIAN HIT");
                Publish(new Message(Time, $"CIVILIAN HIT  +{HeatRules.CivilianHit:0} HEAT", Tone.Bad));
                break;
            case Faction.Police when bullet:
                AddHeat(HeatRules.PoliceShot, "SHOT A POLICE CAR");
                if (Time - _lastPoliceShotMessage >= HeatRules.HitCooldown)
                {
                    _lastPoliceShotMessage = Time;
                    Publish(new Message(Time, $"POLICE CAR SHOT  +{HeatRules.PoliceShot:0} HEAT", Tone.Bad));
                }
                break;
            case Faction.Police when a.HitHeatCooldown <= 0:
                a.HitHeatCooldown = HeatRules.HitCooldown;
                AddHeat(HeatRules.PoliceHit, "HIT A POLICE CAR");
                Publish(new Message(Time, $"POLICE CAR HIT  +{HeatRules.PoliceHit:0} HEAT", Tone.Bad));
                break;
        }
    }

    /// <summary>A civilian or police car died: heat and a Bad message if you hurt it in the last 5 s, otherwise an Info "crashed" message.</summary>
    void PublishNonHostileDestroyed(AgentCar car, Faction faction)
    {
        bool civilian = faction == Faction.Civilian;
        string name = civilian ? "CIVILIAN" : "POLICE CAR";
        if (car.PlayerBlameUntil > Time)
        {
            double heat = civilian ? HeatRules.CivilianDestroyed : HeatRules.PoliceDestroyed;
            AddHeat(heat, civilian ? "CIVILIAN DESTROYED" : "DESTROYED A POLICE CAR");
            Publish(new Message(Time, $"{name} DESTROYED  +{heat:0} HEAT", Tone.Bad));
        }
        else Publish(new Message(Time, $"{name} CRASHED", Tone.Info));
    }

    /// <summary>
    /// Runs the meter's decay for this step, publishing a level change if there is one. Runs in StepOnce after the
    /// collisions. While <see cref="HeatHeldUntilMoved"/> only the rise flash runs; the first step the player drives
    /// faster than 5 mph clears it, and the meter runs from that step on.
    /// </summary>
    void StepHeat()
    {
        if (HeatHeldUntilMoved)
        {
            if (Player.State != VehicleState.Driving || Player.Speed <= BustedWatch.SpeedLimit)
            {
                Heat.UpdateFlash(Step);
                return;
            }
            HeatHeldUntilMoved = false;
        }
        var from = Heat.Level;
        if (Heat.Update(Step, PoliceNear()) is { } to) PublishLevelChange(from, to);
    }

    /// <summary>True when a live police car is within <see cref="HeatRules.NoPoliceRadius"/> m of the player, or one is chasing (not on patrol) and can see.</summary>
    bool PoliceNear()
    {
        foreach (var a in Agents)
        {
            if (a.Faction != Faction.Police || a.State == VehicleState.Wrecked) continue;
            if (Math.Abs(a.Distance - Player.Distance) <= HeatRules.NoPoliceRadius) return true;
            if (!ReferenceEquals(a.Driver, PatrolDriver.Instance) && a.Blinded <= 0) return true;
        }
        return false;
    }

    /// <summary>Publishes the level event and exactly one message (a non-urgent banner can be held back, so the message always goes out), and the banner on a rise.</summary>
    void PublishLevelChange(HeatLevel from, HeatLevel to)
    {
        Publish(new HeatLevelChanged(Time, from, to));
        string name = HeatRules.LevelName(to);
        if (to < from)
        {
            Publish(new Message(Time, $"HEAT DOWN: {name}", Tone.Good));
            return;
        }
        if (to == HeatLevel.Watched)
        {
            Publish(new Message(Time, "HEAT: WATCHED - THE POLICE ARE TAKING NOTE", Tone.Info));
            return;
        }
        Publish(new Message(Time, $"HEAT: {name}", Tone.Bad));
        if (Announcement is { } shown && Time - shown.Start < BannerHoldSeconds) return;
        switch (to)
        {
            case HeatLevel.Patrol: Announce("POLICE PATROLS", "SPEED TRAPS AND ROADBLOCKS AHEAD", 2.5); break;
            case HeatLevel.Pursuit: Announce("POLICE PURSUIT", "DON'T LET THEM STOP YOU", 2.5); break;
            case HeatLevel.Manhunt: Announce("MANHUNT", "THEY WILL RAM YOU - SHAKE THEM WITH SMOKE", 2.5); break;
        }
    }
}
