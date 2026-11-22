using NightPursuit.Core.Hazards;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Simulation;

public sealed partial class World
{
    /// <summary>The beaten boss's crash, from its kill until the run ends; null before the boss dies.</summary>
    public BossFinale? Finale { get; private set; }

    /// <summary>
    /// True from the boss kill on. While it is true nothing can hurt, wreck or bust the player, heat and score stay as
    /// they are, no car fires, and the player's car drives itself (<see cref="FinaleAutopilot"/>).
    /// </summary>
    public bool InFinale => Finale is not null;

    /// <summary>Gives the crash for the boss kill; called once, when the boss dies. The game sets it from its picker.</summary>
    public Func<FinaleChoice> FinaleSource { get; set; } = () => FinaleChoice.Default;

    /// <summary>The lane centre the player's car holds during the finale: the one nearest it at the kill.</summary>
    internal double FinaleLane { get; private set; }

    /// <summary>Starts the beaten boss's crash. It starts at least as fast as the player, so it pulls ahead.</summary>
    void StartFinale(BossCar boss)
    {
        var choice = FinaleSource();
        Finale = new BossFinale(choice, boss.Id, boss.Distance, boss.Lateral, Math.Max(boss.Speed, Player.Speed), boss.SpinAngle);
        FinaleLane = Track.LaneCentres.MinBy(c => Math.Abs(c - Player.Lateral));
        boss.StateTimer = double.MaxValue;   // kept until it explodes (IsGone removes it when the timer is 0)
        boss.PushVel = 0;
        Publish(new BossFinaleStarted(Time, boss.Id, choice.Kind, choice.Side));
        ClearHostileFire();
    }

    /// <summary>Moves the beaten boss along its crash; on the explosion step it blows up onto the verge and is removed.</summary>
    void StepFinaleBoss(BossCar boss)
    {
        var f = Finale!;
        bool exploded = f.Step(Step);
        boss.Distance = f.Distance;
        boss.Lateral = f.Lateral;
        boss.Speed = f.Speed;
        boss.SpinAngle = f.SpinAngle;
        if (!exploded) return;
        Publish(new BossExploded(Time, f.Distance, f.Lateral));
        Hazards.Add(new Wreck
        {
            Distance = f.Distance, PrevDistance = f.Distance, Lateral = f.Lateral,
            Width = boss.Stats.Width, Length = boss.Stats.Length, Speed = 0, Seed = boss.Id,
        });
        boss.StateTimer = 0;
    }

    /// <summary>Once the boss has blown up and gone, the finale still runs on through its hold.</summary>
    void StepFinaleAfterBoss()
    {
        var f = Finale!;
        if (!f.Exploded || f.Done) return;
        foreach (var a in Agents)
            if (a.Id == f.BossId) return;   // still here: stepped with the agents
        f.Step(Step);
    }

    /// <summary>
    /// Ends every enemy missile in flight as missed and removes every enemy bullet: at a respawn, so nothing launched
    /// before the wreck can hit the new car, and at the boss kill, so nothing can hit you during the finale.
    /// </summary>
    internal void ClearHostileFire()
    {
        ClearEnemyMissilesAsMissed();
        Tracers.RemoveAll(t => t.Hostile);
    }

    /// <summary>Takes down the banner on screen now (a "BUSTED" banner from the step the boss died, which no longer applies).</summary>
    internal void ClearAnnouncement() => _announcement = null;
}
