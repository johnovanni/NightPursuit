using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Hazards;

/// <summary>A drifting smoke screen that blinds cars driving through it.</summary>
public sealed class SmokeCloud : Hazard
{
    public const double HalfLength = 15, HalfWidth = 6, Lifetime = 5, Drag = 10;
    public const double ClearBehind = 600;

    public double Age { get; set; }
    public double Speed { get; set; }
    /// <summary>Distance at the start of the latest step, for render interpolation.</summary>
    public double PrevDistance { get; set; }

    public bool Covers(Vehicle v) =>
        Math.Abs(v.Distance - Distance) < HalfLength + v.Stats.Length / 2 &&
        Math.Abs(v.Lateral - Lateral) < HalfWidth + v.Stats.Width / 2;

    public override void Update(double dt, double playerDistance)
    {
        Age += dt;
        Speed = Math.Max(0, Speed - Drag * dt);
        if (Speed < 1e-9) Speed = 0;
        Distance += Speed * dt;
        if (Age >= Lifetime - 1e-9 || Distance < playerDistance - ClearBehind) Expired = true;
    }
}
