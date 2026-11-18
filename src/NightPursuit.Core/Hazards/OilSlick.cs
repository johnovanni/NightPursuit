namespace NightPursuit.Core.Hazards;

/// <summary>A pool of oil on the road. A car driving into it spins out.</summary>
public sealed class OilSlick : Hazard
{
    public const double Radius = 2.4;
    public const double ClearBehind = 600;

    /// <summary>Seconds left before it dries up.</summary>
    public double Life { get; set; } = 25;

    public override void Update(double dt, double playerDistance)
    {
        Life -= dt;
        if (Life <= 0 || Distance < playerDistance - ClearBehind) Expired = true;
    }
}
