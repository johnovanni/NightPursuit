namespace NightPursuit.Core.Hazards;

/// <summary>A burning car left by gunfire. It slides to a stop in its lane (decision D4).</summary>
public sealed class Wreck : Hazard
{
    public const double Friction = 25;
    public const double ClearBehind = 600;

    public double Width { get; set; }
    public double Length { get; set; }
    public double Speed { get; set; }
    /// <summary>Distance at the start of the latest step (the App draws between this and <see cref="Hazard.Distance"/>).</summary>
    public double PrevDistance { get; set; }
    /// <summary>Seconds burning; drives the flicker effect.</summary>
    public double Burn { get; set; }
    public bool HitPlayer { get; set; }

    public override void Update(double dt, double playerDistance)
    {
        Burn += dt;
        if (Speed > 0)
        {
            Speed = Math.Max(0, Speed - Friction * dt);
            Distance += Speed * dt;
        }
        if (Distance < playerDistance - ClearBehind) Expired = true;
    }
}
