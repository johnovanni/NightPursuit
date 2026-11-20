namespace NightPursuit.Core.Session;

public enum TextSize { Normal, Large, Largest }

/// <summary>Player choices that last between games.</summary>
public sealed class GameSettings
{
    /// <summary>0 = RampPedal, 1 = SpeedLever.</summary>
    public int SchemeIndex { get; set; }
    public bool LighterSteering { get; set; } = true;
    public bool ScreenShake { get; set; } = true;
    public TextSize TextSize { get; set; } = TextSize.Normal;
    public string LastInitials { get; set; } = "AAA";

    public double TextScale => TextSize switch { TextSize.Large => 1.25, TextSize.Largest => 1.5, _ => 1.0 };

    public GameSettings Clone() => new()
    {
        SchemeIndex = SchemeIndex, LighterSteering = LighterSteering, ScreenShake = ScreenShake,
        TextSize = TextSize, LastInitials = LastInitials,
    };
}
