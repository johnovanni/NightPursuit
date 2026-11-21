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
    /// <summary>Master switch for all sound (the M key).</summary>
    public bool SoundOn { get; set; } = true;
    /// <summary>Music volume, 0 to 10.</summary>
    public int MusicVolume { get; set; } = 7;
    /// <summary>Effects volume, 0 to 10.</summary>
    public int EffectsVolume { get; set; } = 8;
    public string LastInitials { get; set; } = "AAA";

    public double TextScale => TextSize switch { TextSize.Large => 1.25, TextSize.Largest => 1.5, _ => 1.0 };

    public GameSettings Clone() => new()
    {
        SchemeIndex = SchemeIndex, LighterSteering = LighterSteering, ScreenShake = ScreenShake,
        TextSize = TextSize, SoundOn = SoundOn, MusicVolume = MusicVolume, EffectsVolume = EffectsVolume, LastInitials = LastInitials,
    };
}
