using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Simulation;

public sealed partial class World
{
    const double EnemyMuzzleSpeed = 160, EnemyRange = 120, EnemyTracerMargin = 0.15;

    /// <summary>Fires an enemy car's weapons for the intent it just drove; runs after the car has moved.</summary>
    void StepAgentWeapons(AgentCar a, DriveIntent intent)
    {
        if (a.Arms.RearGuns is { } guns && guns.TryFire(intent.Fire, Step))
        {
            double d = a.Distance - a.Stats.Length / 2 - 0.3;
            Tracers.Add(new Tracer
            {
                Pose = new RoadPose(d, a.Lateral, Math.PI),
                PrevDistance = d, PrevLateral = a.Lateral, PrevHeading = Math.PI,   // created after the Prev refresh: drawn where it starts
                Speed = Math.Max(20, EnemyMuzzleSpeed - a.Speed),
                MuzzleSpeed = EnemyMuzzleSpeed, Range = EnemyRange,
                OwnerId = a.Id, Hostile = true,
            });
        }
    }

    /// <summary>An enemy bullet only ever tests the player; it passes through other cars.</summary>
    void StepHostileTracer(Tracer t)
    {
        if (Player.State == VehicleState.Wrecked) return;
        if (!SweptHit(t.PrevDistance, t.PrevLateral, t.Pose.Distance, t.Pose.Lateral, Player, EnemyTracerMargin)) return;
        t.Dead = true;
        DamagePlayer(2, "SHOT BY A GUNNER", messageGap: 1.0);
    }
}
