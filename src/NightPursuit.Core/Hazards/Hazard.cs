namespace NightPursuit.Core.Hazards;

/// <summary>Something lying in the road that is not a car.</summary>
public abstract class Hazard
{
    public double Distance { get; set; }
    public double Lateral { get; set; }
    public bool Expired { get; set; }

    public abstract void Update(double dt, double playerDistance);
}
