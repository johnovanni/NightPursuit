using NightPursuit.Core.Missions;
using NightPursuit.Core.Police;
using NightPursuit.Core.Session;
using NightPursuit.Core.Simulation;
using NightPursuit.Core.Vehicles;
using NightPursuit.Core.Weapons;

namespace NightPursuit.Core.Audio;

/// <summary>What the sound should do this frame: the one-off cues to start, the steady sounds and the music.</summary>
public sealed record SoundFrame(IReadOnlyList<CuePlay> Cues, LiveState Live, MusicCue Music);

/// <summary>
/// Turns what the game shows into sound, once a frame (design §5). It only reads the flow and the World: it never
/// writes to the World, steps it or touches its random numbers, so the game plays the same with or without it.
/// Every cue repeats something already on screen (NFR-3).
/// </summary>
public sealed class SoundDirector(MusicSlots music)
{
    /// <summary>Sounds from a car this far away (metres along the road) or further are not heard.</summary>
    public const double SilentFrom = 400;
    /// <summary>A car this far away is heard at half volume.</summary>
    public const double HalfVolumeAt = 60;
    /// <summary>A car this far to one side (metres) is heard fully from that side.</summary>
    public const double FullPanAt = 10;
    /// <summary>The missile whine starts at this distance.</summary>
    public const double WhineRange = 150;
    /// <summary>The same warning is not sounded again within this many real seconds.</summary>
    public const double WarningGap = 1.5;
    const double OffRoadFullSpeed = 30;   // m/s

    readonly List<CuePlay> _cues = new();
    readonly CueGaps _gaps = new();
    readonly double[] _warnedAt = Filled(Enum.GetValues<WarningKind>().Length);
    double _now;   // real seconds since the director started

    // The World being watched, as EffectList.Absorb does: reset on a new World, read once per new Time.
    World? _world;
    double _absorbedTime = double.NaN;
    HashSet<Tracer> _seenTracers = new(), _spareTracers = new();   // swapped each read so no set is allocated
    Dictionary<int, (double Distance, double Lateral)> _known = new(), _spareKnown = new();   // car positions at the last read

    // The menus as they were last frame.
    MenuInput? _menuKey;
    int _version = -1;
    Screen? _screen;
    MenuModel? _menu;
    int _selected;
    readonly List<string?> _values = new();
    InitialsEntry? _initials;
    string? _initialsText;

    /// <summary>The App calls this for each menu key it hands to the flow this frame, before <see cref="Update"/>.</summary>
    public void NoteMenuKey(MenuKey k)
    {
        if (k.Input is MenuInput.Confirm or MenuInput.Back) _menuKey = k.Input;
    }

    /// <summary>Works out this frame's sound. <paramref name="realSeconds"/> is the real time since the last call.</summary>
    public SoundFrame Update(ScreenFlow flow, double realSeconds)
    {
        if (realSeconds > 0) _now += realSeconds;
        _cues.Clear();
        if (flow.Game?.Run?.World is { } w) Absorb(w);
        Menus(flow);
        var cues = _cues.Count == 0 ? Array.Empty<CuePlay>() : _cues.ToArray();
        return new SoundFrame(cues, Live(flow), music.Update(flow));
    }

    // ---- the World's events and guns ----

    void Absorb(World w)
    {
        if (!ReferenceEquals(w, _world))
        {
            _world = w;
            _absorbedTime = double.NaN;
            _seenTracers.Clear();
            _known.Clear();
        }
        if (w.Time == _absorbedTime) return;   // no new steps since the last read: these are the same events
        _absorbedTime = w.Time;

        Guns(w);
        Events(w);
        Remember(w);
    }

    // A tracer not seen before is a new shot, however far it flew during a long frame.
    void Guns(World w)
    {
        var seen = _spareTracers;
        seen.Clear();
        bool playerShot = false;
        double enemyVolume = 0, enemyPan = 0;
        foreach (var t in w.Tracers)
        {
            bool mine = !t.Hostile && t.OwnerId == w.Player.Id;
            if (!mine && !t.Hostile) continue;
            seen.Add(t);
            if (_seenTracers.Contains(t)) continue;
            if (mine) playerShot = true;
            else if (Heard(w, t.OwnerId, out double v, out double p) && v > enemyVolume) (enemyVolume, enemyPan) = (v, p);
        }
        _spareTracers = _seenTracers;
        _seenTracers = seen;
        if (playerShot) Play(SoundCue.GunPlayer);
        if (enemyVolume > 0) Play(SoundCue.GunEnemy, enemyVolume, enemyPan);   // the loudest new enemy shot
    }

    void Events(World w)
    {
        var events = w.Events;
        bool busted = false;
        for (int i = 0; i < events.Count; i++)
            if (events[i] is PlayerBusted) busted = true;

        for (int i = 0; i < events.Count; i++)
        {
            switch (events[i])
            {
                case WeaponUsed u: From(w, u.OwnerId, WeaponCue(u.Weapon)); break;
                case StrikeLanded: Play(SoundCue.StrikeBoom); break;
                case VehicleDestroyed { Kind: AgentKind.Boss }: break;   // the boss blows up at the end of its crash (BossExploded)
                case VehicleDestroyed d: From(w, d.VehicleId, SoundCue.Explosion); break;
                case BossFinaleStarted f: From(w, f.VehicleId, SoundCue.Spin); break;
                case BossExploded x: At(w, x.Distance, x.Lateral, SoundCue.Explosion); break;
                case PlayerDamaged d: Play(SoundCue.PlayerHit, Math.Clamp(0.5 + d.Amount / 40, 0.5, 1)); break;
                case PlayerWrecked when !busted: Play(SoundCue.PlayerWrecked); break;   // busted has its own sound
                case PlayerBusted: Play(SoundCue.PlayerBusted); break;
                case VehiclePunctured p: From(w, p.VehicleId, SoundCue.Puncture); break;
                case VehicleSpun s: From(w, s.VehicleId, SoundCue.Spin); break;
                case SupplyCollected s: Play(s.Item == SupplyItem.Armor ? SoundCue.PickupArmor : SoundCue.Pickup); break;
                case WarningRaised r: Warn(r.Kind); break;
                case BossAppeared: Play(SoundCue.BossAppears); break;
                case BossPhaseChanged: Play(SoundCue.BossPhase); break;
                case BossDamaged: Play(SoundCue.BossHit); break;
                case EnemyMissileLaunched: Play(SoundCue.EnemyMissileLaunch); break;
                case EnemyMissileEnded e: Play(MissileEndCue(e.Result)); break;
                case HeatLevelChanged h: Play(h.To > h.From ? SoundCue.HeatUp : SoundCue.HeatDown); break;
                case SpeedTrapPassed: Play(SoundCue.SpeedTrap); break;
                case RoadblockHit: Play(SoundCue.RoadblockCrash); break;
                // Message and HeatChanged have no sound of their own: they come with one of the events above.
            }
        }
    }

    static SoundCue WeaponCue(WeaponKind k) => k switch
    {
        WeaponKind.Oil => SoundCue.OilDrop,
        WeaponKind.Smoke => SoundCue.SmokeDrop,
        WeaponKind.Caltrops => SoundCue.CaltropsDrop,
        WeaponKind.Missile => SoundCue.MissileLaunch,
        _ => SoundCue.StrikeCall,
    };

    static SoundCue MissileEndCue(MissileResult r) => r switch
    {
        MissileResult.Hit => SoundCue.Explosion,
        MissileResult.Dodged => SoundCue.EnemyMissileDodged,
        _ => SoundCue.EnemyMissileShotDown,
    };

    void Warn(WarningKind kind)
    {
        if (_now - _warnedAt[(int)kind] < WarningGap) return;
        if (Play(kind == WarningKind.MissileIncoming ? SoundCue.MissileAlarm : SoundCue.Warning)) _warnedAt[(int)kind] = _now;
    }

    // Keeps where every car was, so a car destroyed and removed in the next steps is still heard from its side.
    void Remember(World w)
    {
        var now = _spareKnown;
        now.Clear();
        foreach (var a in w.Agents) now[a.Id] = (a.Distance, a.Lateral);
        _spareKnown = _known;
        _known = now;
    }

    // ---- distance and side ----

    void From(World w, int vehicleId, SoundCue cue)
    {
        if (Heard(w, vehicleId, out double volume, out double pan)) Play(cue, volume, pan);
    }

    /// <summary>A sound from a place on the road rather than from a car.</summary>
    void At(World w, double distance, double lateral, SoundCue cue)
    {
        var p = w.Player;
        if (Place(distance - p.Distance, lateral - p.Lateral, out double volume, out double pan)) Play(cue, volume, pan);
    }

    /// <summary>
    /// How loud and from which side a sound from car <paramref name="id"/> is: false when it is too far to hear.
    /// The player's own sounds, and a car never seen, play centred at full volume.
    /// </summary>
    bool Heard(World w, int id, out double volume, out double pan)
    {
        var p = w.Player;
        if (id != p.Id && Locate(w, id, out double distance, out double lateral))
            return Place(distance - p.Distance, lateral - p.Lateral, out volume, out pan);
        (volume, pan) = (1, 0);
        return true;
    }

    bool Locate(World w, int id, out double distance, out double lateral)
    {
        foreach (var a in w.Agents)
            if (a.Id == id)
            {
                (distance, lateral) = (a.Distance, a.Lateral);
                return true;
            }
        if (_known.TryGetValue(id, out var last))
        {
            (distance, lateral) = last;
            return true;
        }
        (distance, lateral) = (0, 0);
        return false;
    }

    /// <summary>
    /// Volume 1 / (1 + (d / 60)²) for a car <paramref name="ahead"/> m along the road (either way), silent from 400 m;
    /// pan from its offset to the right (<paramref name="right"/>, the side the Projector draws a larger Lateral).
    /// </summary>
    static bool Place(double ahead, double right, out double volume, out double pan)
    {
        double d = Math.Abs(ahead);
        if (d >= SilentFrom)
        {
            (volume, pan) = (0, 0);
            return false;
        }
        double r = d / HalfVolumeAt;
        volume = 1 / (1 + r * r);
        pan = Math.Clamp(right / FullPanAt, -1, 1);
        return true;
    }

    bool Play(SoundCue cue, double volume = 1, double pan = 0)
    {
        // The same cue twice in one frame plays once, as the loudest of them (with its own side).
        for (int i = 0; i < _cues.Count; i++)
            if (_cues[i].Cue == cue)
            {
                if (volume > _cues[i].Volume) _cues[i] = new CuePlay(cue, volume, pan);
                return false;
            }
        if (!_gaps.TryStart(cue, _now)) return false;
        _cues.Add(new CuePlay(cue, volume, pan));
        return true;
    }

    // ---- steady sounds ----

    static LiveState Live(ScreenFlow flow)
    {
        if (flow.Current != Screen.Play || flow.Game?.Run is not { } run
            || run.State is not (RunState.Playing or RunState.Respawning)) return LiveState.Silent;
        var w = run.World;
        var p = w.Player;
        double speed = Math.Max(0, p.Speed);

        double siren = 0, sirenPan = 0, nearestPolice = double.PositiveInfinity;
        if (w.Heat.Level >= HeatLevel.Pursuit)
            foreach (var a in w.Agents)
            {
                if (a.Faction != Faction.Police || !a.IsOnRoadDriving) continue;
                double ahead = a.Distance - p.Distance;
                if (Math.Abs(ahead) >= nearestPolice || !Place(ahead, a.Lateral - p.Lateral, out double v, out double pan)) continue;
                (nearestPolice, siren, sirenPan) = (Math.Abs(ahead), v, pan);
            }

        double nearestMissile = WhineRange;
        foreach (var m in w.EnemyMissiles)
            nearestMissile = Math.Min(nearestMissile, Math.Abs(m.Distance - p.Distance));
        bool whine = nearestMissile < WhineRange;

        return new LiveState
        {
            EngineOn = p.State != VehicleState.Wrecked && run.State != RunState.Respawning,
            // VehicleStats.Player.TopSpeed is 0 (the player's top speed lives in the feel settings), so use that.
            Speed01 = Math.Clamp(speed / w.Feel.MaxSpeed, 0, 1),
            Throttle01 = p.Throttle,
            Punctured = p.PunctureLeft > 0,
            OffRoad01 = p.OffRoad ? Math.Min(1, speed / OffRoadFullSpeed) : 0,
            Skid01 = p.GripLoss > 0 ? 1 : 0,
            Siren01 = siren,
            SirenPan = sirenPan,
            Whine01 = whine ? 1 : 0,
            WhineCloseness01 = whine ? 1 - nearestMissile / WhineRange : 0,
        };
    }

    // ---- menus ----

    void Menus(ScreenFlow flow)
    {
        var key = _menuKey;
        _menuKey = null;
        if (flow.Version == _version) return;   // nothing on the menus has changed
        _version = flow.Version;

        var screen = flow.Current;
        var menu = flow.Menu;
        if (_screen is { } last)
        {
            if (screen != last)
            {
                if (key == MenuInput.Confirm) Play(SoundCue.MenuConfirm);
                else if (key == MenuInput.Back) Play(SoundCue.MenuBack);
            }
            else
            {
                if (menu is not null && _menu is not null && menu.Selected != _selected) Play(SoundCue.MenuMove);
                // A settings change rebuilds the menu with the new value: heard at the new level.
                if (screen == Screen.Settings && menu is not null && !ReferenceEquals(menu, _menu) && ValuesDiffer(menu)) Play(SoundCue.MenuConfirm);
                if (screen == Screen.EnterInitials && flow.Initials is { } e && ReferenceEquals(e, _initials) && e.Text != _initialsText)
                    Play(SoundCue.MenuLetter);
            }
        }

        _screen = screen;
        _selected = menu?.Selected ?? 0;
        if (!ReferenceEquals(menu, _menu))
        {
            _values.Clear();
            if (menu is not null)
                foreach (var item in menu.Items) _values.Add(item.Value);
        }
        _menu = menu;
        _initials = flow.Initials;
        _initialsText = screen == Screen.EnterInitials ? flow.Initials?.Text : null;
    }

    bool ValuesDiffer(MenuModel menu)
    {
        if (menu.Items.Count != _values.Count) return false;   // a different menu, not a changed value
        for (int i = 0; i < _values.Count; i++)
            if (menu.Items[i].Value != _values[i]) return true;
        return false;
    }

    static double[] Filled(int n)
    {
        var a = new double[n];
        Array.Fill(a, double.NegativeInfinity);
        return a;
    }
}

/// <summary>Keeps each cue from starting again too soon (in real seconds), so bursts do not pile up (design §5.4).</summary>
sealed class CueGaps
{
    readonly double[] _startedAt = new double[Enum.GetValues<SoundCue>().Length];

    public CueGaps() => Array.Fill(_startedAt, double.NegativeInfinity);

    /// <summary>The least time between two starts of the same cue.</summary>
    public static double MinGap(SoundCue c) => c switch
    {
        SoundCue.PlayerHit => 0.25,
        SoundCue.BossHit => 0.2,
        SoundCue.GunPlayer or SoundCue.GunEnemy => 0.06,
        SoundCue.Explosion => 0.08,
        _ => 0.05,
    };

    /// <summary>True (and the start is noted) when <paramref name="cue"/> may start at <paramref name="now"/>.</summary>
    public bool TryStart(SoundCue cue, double now)
    {
        if (now - _startedAt[(int)cue] < MinGap(cue)) return false;
        _startedAt[(int)cue] = now;
        return true;
    }
}
