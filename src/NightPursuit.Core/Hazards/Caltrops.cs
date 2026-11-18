using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Hazards;

/// <summary>Spikes scattered across a lane; a car driving over them can have a tyre punctured.</summary>
public sealed class Caltrops : Hazard
{
    public const double HalfWidth = 2.5, HalfLength = 1.5, Lifetime = 30;
    public const double ClearBehind = 600;

    public double Life { get; set; } = Lifetime;
    public bool HitPlayer { get; set; }

    public bool Covers(Vehicle v) =>
        Math.Abs(v.Distance - Distance) < HalfLength + v.Stats.Length / 2 &&
        Math.Abs(v.Lateral - Lateral) < HalfWidth + v.Stats.Width / 2;

    public override void Update(double dt, double playerDistance)
    {
        Life -= dt;
        if (Life <= 0 || Distance < playerDistance - ClearBehind) Expired = true;
    }
}
