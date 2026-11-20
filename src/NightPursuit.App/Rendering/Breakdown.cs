using System.Windows;

namespace NightPursuit.App.Rendering;

/// <summary>
/// SPIKE: how far the driver's car has broken down, from the seconds since it was wrecked. Presentation only: it is
/// worked out from a clock in the view, never from the simulation, so it also runs on the frozen Game Over screen.
/// Over the first moments the windshield gives out and the hood crumples; then the car leans and dips as it dies, the
/// dashboard flickers twice and goes dark, smoke and small flames rise from the hood, and a faint double image circles
/// the view (the driver is dizzy). Nothing flashes fast: two dashboard dips in about 0.7 s, slow flame and smoke motion.
/// </summary>
internal readonly record struct Breakdown(double Seconds, bool Active)
{
    public static Breakdown None => new(0, false);
    public static Breakdown After(double seconds) => new(Math.Max(0, seconds), true);

    static double Ease(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    /// <summary>0 to 1: the windshield's cracks jump to a full web, with a faint milky haze.</summary>
    public double Shatter => Active ? Math.Clamp(Seconds / 0.35, 0, 1) : 0;
    /// <summary>0 to 1: the bonnet line buckles and one gun barrel bends down.</summary>
    public double Crumple => Active ? Ease(Seconds / 0.3) : 0;
    /// <summary>0 to 1: the nose dips and the view leans as the car slides to a stop.</summary>
    public double Lean => Active ? Ease(Seconds / 1.4) : 0;
    public const double LeanDegrees = 4, DipOfHeight = 0.035;

    /// <summary>The dashboard's brightness: full, two dips, then dark (but still faintly there).</summary>
    public double Dash => !Active ? 1 : Seconds switch
    {
        < 0.5 => 1, < 0.65 => 0.3, < 0.85 => 0.85, < 1.0 => 0.2, < 1.2 => 0.55, _ => Math.Max(0.12, 0.55 - (Seconds - 1.2) * 0.6),
    };

    /// <summary>0 to 1: the strength of the dizzy double image.</summary>
    public double Dizzy => Active ? Ease((Seconds - 0.3) / 0.6) : 0;
    public const double GhostOpacity = 0.42, GhostRadiusOfWidth = 0.012, GhostTurnSeconds = 1.4;

    /// <summary>Where the double image sits: on a tight circle around the real view, turning once every 1.4 s.</summary>
    public Vector GhostOffset(double width)
    {
        double r = GhostRadiusOfWidth * width * Dizzy, a = Seconds * Math.Tau / GhostTurnSeconds;
        return new Vector(Math.Cos(a) * r, Math.Sin(a) * r);
    }
}
