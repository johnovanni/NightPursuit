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

    public Game(IReadOnlyList<MissionDefinition> missions, FeelSettings feel, GameSettings settings, bool practice = false, int seed = 1)
    {
        if (missions.Count == 0) throw new ArgumentException("A game needs at least one mission.", nameof(missions));
        Missions = missions;
        _feel = feel;
        _settings = settings;
        Practice = practice;
        _seed = seed;
    }

    public IReadOnlyList<MissionDefinition> Missions { get; }
    public int MissionIndex { get; private set; }
    public MissionDefinition Mission => Missions[MissionIndex];
    public bool Practice { get; }
    public GameStage Stage { get; private set; } = GameStage.Briefing;
    /// <summary>Null in Briefing; the last run is kept afterwards so the frozen road can show behind a screen.</summary>
    public MissionRun? Run { get; private set; }
    public MissionBonus? LastBonus { get; private set; }
    public bool QuitEarly { get; private set; }

    public int Score => Stage == GameStage.Playing && Run is not null ? Run.World.Score : _score;
    public int SpareCars => Stage == GameStage.Playing && Run is not null ? Run.World.Player.SpareCars : _spareCars;
    public int MissionReached => MissionIndex + 1;
    public bool FinishedAll => Stage == GameStage.Finished && !Practice;

    public void StartMission()
    {
        if (Stage != GameStage.Briefing) return;
        Run = new MissionRun(Mission, _feel, _seed + MissionIndex, new RunStart(_score, _spareCars, _settings.SchemeIndex));
        Stage = GameStage.Playing;
    }

    public void Update(ControlFrame held, double seconds)
    {
        if (Stage != GameStage.Playing || Run is null) return;
        Run.Update(held, seconds);
        if (Run.State == RunState.Completed)
        {
            bool carLost = Run.World.Player.SpareCars < Run.Start.SpareCars;
            LastBonus = MissionBonus.For(Mission, Run.World.Player.Armor, carLost);
            _score = Run.World.Score + LastBonus.Total;
            _spareCars = Run.World.Player.SpareCars;
            Stage = GameStage.MissionComplete;
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
            if (Practice) RestartMission();
            return;
        }
        Run.Handle(a);
    }

    /// <summary>Practice only: start the mission over (from a pause or an end state).</summary>
    public void RestartMission()
    {
        if (!Practice || Stage != GameStage.Playing || Run is null || !Run.CanRestart) return;
        Run.Handle(new GameAction(ActionKind.Restart));
        // The rebuild restores the scheme the run started with; the player may have changed it since.
                Run.World.Handle(new GameAction(ActionKind.SelectScheme, _settings.SchemeIndex));
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
