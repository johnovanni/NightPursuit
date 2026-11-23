using NightPursuit.Core.Common;
using NightPursuit.Core.Control;
using NightPursuit.Core.Missions;
using NightPursuit.Core.Simulation;

namespace NightPursuit.Core.Session;

/// <summary>One play-through: the missions in order, with score and spare cars carried from one to the next.</summary>
public sealed class Game
{
    readonly FeelSettings _feel;
    readonly GameSettings _settings;
    readonly int _seed;
    int _score;
    int _spareCars = 3;

    /// <param name="finales">Picks each boss crash for the whole game (the App seeds one from the clock); null makes one
    /// from <paramref name="seed"/>.</param>
    public Game(IReadOnlyList<MissionDefinition> missions, FeelSettings feel, GameSettings settings, bool practice = false, int seed = 1,
        FinalePicker? finales = null)
    {
        if (missions.Count == 0) throw new ArgumentException("A game needs at least one mission.", nameof(missions));
        Missions = missions;
        _feel = feel;
        _settings = settings;
        Practice = practice;
        _seed = seed;
        Finales = finales ?? new FinalePicker(new SeededRng(seed));
    }

    /// <summary>Picks every boss crash in this game, so no two in a row are alike, restarts included.</summary>
    public FinalePicker Finales { get; }

    public IReadOnlyList<MissionDefinition> Missions { get; }
    public int MissionIndex { get; private set; }
    public MissionDefinition Mission => Missions[MissionIndex];
    public bool Practice { get; }
    public GameStage Stage { get; private set; } = GameStage.Briefing;
    /// <summary>Null in Briefing; the last run is kept afterwards so the frozen road can show behind a screen.</summary>
    public MissionRun? Run { get; private set; }
    public MissionBonus? LastBonus { get; private set; }
    /// <summary>Spare cars the last mission bonus earned (the 25,000-point marks it passed), for the results screen.</summary>
    public int LastExtraCars { get; private set; }
    public bool QuitEarly { get; private set; }
    /// <summary>True when the last run ended with the boss getting away (the failed and game-over screens say so).</summary>
    public bool BossEscaped => Run?.State == RunState.Escaped;

    public int Score => Stage == GameStage.Playing && Run is not null ? Run.World.Score : _score;
    public int SpareCars => Stage == GameStage.Playing && Run is not null ? Run.World.Player.SpareCars : _spareCars;
    public int MissionReached => MissionIndex + 1;
    public bool FinishedAll => Stage == GameStage.Finished && !Practice;

    public void StartMission()
    {
        if (Stage != GameStage.Briefing) return;
        Run = new MissionRun(Mission, _feel, _seed + MissionIndex, new RunStart(_score, _spareCars, _settings.SchemeIndex), Finales.Next);
        LastExtraCars = 0;
        Stage = GameStage.Playing;
    }

    public void Update(ControlFrame held, double seconds)
    {
        if (Stage != GameStage.Playing || Run is null) return;
        Run.Update(held, seconds);
        if (Run.State == RunState.Completed)
        {
            bool carLost = Run.World.Player.CarsLost > 0;   // not spare cars at start and end: an extra car would hide a loss
            LastBonus = MissionBonus.For(Mission, Run.World.Player.Armor, carLost);
            _score = Run.World.Score + LastBonus.Total;
            _spareCars = Run.World.Player.SpareCars;
            LastExtraCars = ExtraCars.Kept(ExtraCars.Earned(Run.World.Score, _score), _spareCars);
            _spareCars += LastExtraCars;
            Stage = GameStage.MissionComplete;
        }
        else if (Run.State == RunState.Escaped)
        {
            // You keep the points; the escape costs a spare car, and with none left the game is over (owner, 2026-11-23).
            _score = Run.World.Score;
            int spares = Run.World.Player.SpareCars;
            _spareCars = Math.Max(0, spares - 1);
            Stage = spares <= 0 ? GameStage.GameOver : GameStage.MissionFailed;
        }
        else if (Run.State == RunState.GameOver)
        {
            Carry();
            Stage = GameStage.GameOver;
        }
    }

    public void Handle(GameAction a)
    {
        if (Stage != GameStage.Playing || Run is null) return;
        if (a.Kind == ActionKind.Restart)
        {
            if (Practice && !RestartMission()) Run.Handle(a);   // not restartable now: the run says why
            return;
        }
        Run.Handle(a);
    }

    /// <summary>Practice only: start the mission over (from a pause, or from the mission-complete or game-over screen).</summary>
    /// <returns>True when the mission was restarted.</returns>
    public bool RestartMission()
    {
        if (!Practice || Run is null || !Run.CanRestart) return false;
        if (Stage is not (GameStage.Playing or GameStage.MissionComplete or GameStage.GameOver or GameStage.MissionFailed)) return false;
        Run.Handle(new GameAction(ActionKind.Restart));
        Stage = GameStage.Playing;
        QuitEarly = false;
        // The rebuild restores the scheme the run started with; the player may have changed it since.
        Run.World.Handle(new GameAction(ActionKind.SelectScheme, _settings.SchemeIndex));
        return true;
    }

    /// <summary>After the boss escaped: back to the same mission's briefing, with the score and spare cars it left.</summary>
    public void RetryMission()
    {
        if (Stage != GameStage.MissionFailed) return;
        Run = null;
        Stage = GameStage.Briefing;
    }

    public void Continue()
    {
        if (Stage != GameStage.MissionComplete) return;
        if (MissionIndex + 1 < Missions.Count)
        {
            MissionIndex++;
            Run = null;
            Stage = GameStage.Briefing;
        }
        else Stage = GameStage.Finished;
    }

    public void Quit()
    {
        if (Stage is not (GameStage.Briefing or GameStage.Playing)) return;
        Carry();
        QuitEarly = true;
        Stage = GameStage.GameOver;
    }

    void Carry()
    {
        if (Run is null) return;
        _score = Run.World.Score;
        _spareCars = Run.World.Player.SpareCars;
    }
}
