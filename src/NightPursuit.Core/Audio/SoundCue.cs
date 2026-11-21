using System.Text;

namespace NightPursuit.Core.Audio;

/// <summary>Every one-off sound effect. Each one has a WAV file in <c>content/sounds</c>.</summary>
public enum SoundCue
{
    // Weapons
    GunPlayer, GunEnemy, OilDrop, SmokeDrop, CaltropsDrop, MissileLaunch, StrikeCall, StrikeBoom,
    // Damage
    Explosion, PlayerHit, PlayerWrecked, PlayerBusted, Puncture, Spin,
    // Pickups and warnings
    Pickup, PickupArmor, Warning, MissileAlarm,
    // Boss
    BossAppears, BossPhase, BossHit,
    // Enemy missiles
    EnemyMissileLaunch, EnemyMissileDodged, EnemyMissileShotDown,
    // Police and road
    HeatUp, HeatDown, SpeedTrap, RoadblockCrash,
    // Menus
    MenuMove, MenuConfirm, MenuBack, MenuLetter,
}

/// <summary>File names and length limits for the <see cref="SoundCue"/> effects.</summary>
public static class SoundCues
{
    /// <summary>The WAV file name for a cue: kebab case, for example <c>gun-player.wav</c>.</summary>
    public static string FileName(SoundCue c)
    {
        var name = c.ToString();
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('-');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.Append(".wav").ToString();
    }

    /// <summary>The longest this cue's file may be, in seconds.</summary>
    public static double MaxSeconds(SoundCue c) => c switch
    {
        SoundCue.PlayerWrecked or SoundCue.PlayerBusted or SoundCue.StrikeBoom or SoundCue.BossAppears => 4.0,
        SoundCue.Explosion or SoundCue.RoadblockCrash => 1.5,
        _ => 0.8,
    };
}
