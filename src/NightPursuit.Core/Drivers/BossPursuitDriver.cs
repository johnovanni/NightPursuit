using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

public enum BossPursuitPhase { Attack, Overtake, HoldAhead, DropBack }

/// <summary>
/// A boss in a Pursuer phase (call 27). It comes alongside and rams as a pursuer does, then overtakes in a lane away
/// from you, pulls at least 60 m ahead and holds about 70 m for 2.5 s, then drops back toward you for the next attack,
/// so each cycle leaves it in front of your guns for a while. Ordinary pursuers keep <see cref="PursuerDriver"/>.
/// </summary>
public sealed class BossPursuitDriver : IDriver
{
    const double AttackWithin = 60, LevelWithin = 3;
    const double OvertakeTopSpeed = 88, OvertakeExtra = 12, PulledAhead = 60;
    const double HoldSeconds = 2.5, HoldGapWanted = 70, HoldGain = 0.8, HoldRange = 10;
    const double DropBackSlower = 8, DropBackUntil = 8;

    public static readonly BossPursuitDriver Instance = new();

    public sealed class State
    {
        public BossPursuitPhase Phase { get; set; }
        public double Timer { get; set; }
        /// <summary>In Attack: true once it has come level with the player (|ΔD| &lt; 3) and the attack clock is running.</summary>
        public bool Alongside { get; set; }
        public double Target { get; set; }
        public AvoidMemory Avoid { get; } = new();
    }

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        var p = view.Player;
        double dD = self.Distance - p.Distance;
        if (self.DriverState is not State s)
        {
            self.DriverState = s = new State { Target = self.Lateral };
            if (dD <= AttackWithin) BeginAttack(self, s, p);
            else BeginDropBack(self, s, p);
        }

        switch (s.Phase)
        {
            case BossPursuitPhase.Attack:
                // As a pursuer does: hold 2.7 m off until level, only then start the clock and, later, the ram.
                if (s.Alongside) s.Timer = DriverHelpers.Countdown(s.Timer, dt);
                else if (Math.Abs(dD) < LevelWithin) { s.Alongside = true; s.Timer = DriverHelpers.AttackSeconds; }
                if (self.ContactCooldown > 0 || (s.Alongside && s.Timer <= 0))
                {
                    s.Phase = BossPursuitPhase.Overtake;
                    s.Target = DriverHelpers.PassingLane(self, p);
                }
                break;
            case BossPursuitPhase.Overtake:
                if (dD >= PulledAhead) { s.Phase = BossPursuitPhase.HoldAhead; s.Timer = HoldSeconds; }
                break;
            case BossPursuitPhase.HoldAhead:
                s.Timer = DriverHelpers.Countdown(s.Timer, dt);
                if (s.Timer <= 0) BeginDropBack(self, s, p);
                break;
            default:
                if (dD <= DropBackUntil) BeginAttack(self, s, p);
                break;
        }

        double limit = DriverHelpers.OnRoadLimit(self);
        double speed;
        switch (s.Phase)
        {
            case BossPursuitPhase.Attack:
                speed = DriverHelpers.AttackSpeed(p.Speed, dD);
                s.Target = DriverHelpers.AttackLateral(p.Lateral, self.Side,
                    ramming: s.Alongside && s.Timer < DriverHelpers.RamSeconds, limit);
                break;
            case BossPursuitPhase.Overtake:
                speed = Math.Min(OvertakeTopSpeed, p.Speed + OvertakeExtra);
                s.Target = DriverHelpers.PassingLane(self, p, s.Target);
                break;
            case BossPursuitPhase.HoldAhead:
                speed = DriverHelpers.HoldGap(p.Speed, dD, HoldGapWanted, HoldGain, -HoldRange, HoldRange);
                break;                                   // keeps its lane
            default:
                speed = p.Speed - DropBackSlower;
                s.Target = DriverHelpers.AttackLateral(p.Lateral, self.Side, ramming: false, limit);
                break;
        }

        double lateral = DriverHelpers.SteerAroundWrecks(self, view, s.Target, s.Avoid, DriverHelpers.ReactionDelay(self.Kind), dt);
        return new DriveIntent(speed, lateral);
    }

    /// <summary>
    /// Starts an attack from the side of the player the car is on (its own <see cref="AgentCar.Side"/> when level), or
    /// from the other side when there is no room for it 2.7 m off the player on the road.
    /// </summary>
    static void BeginAttack(AgentCar self, State s, PlayerCar p)
    {
        s.Phase = BossPursuitPhase.Attack;
        s.Alongside = false;
        s.Timer = 0;
        self.Side = DriverHelpers.SideOf(self, p.Lateral);
        UseSideWithRoom(self, p);
    }

    static void BeginDropBack(AgentCar self, State s, PlayerCar p)
    {
        s.Phase = BossPursuitPhase.DropBack;
        UseSideWithRoom(self, p);
    }

    /// <summary>
    /// Attacks and drops back on the side of the player that has room. It switches sides when 2.7 m off on its side is
    /// past the road edge (the clamp would park it almost straight behind or ahead of the player), or when its side has
    /// no lane at least 2.5 m from the player to pass in afterwards while the other side has one.
    /// </summary>
    static void UseSideWithRoom(AgentCar self, PlayerCar p)
    {
        bool offRoad = Math.Abs(p.Lateral + self.Side * DriverHelpers.AttackOffset) > DriverHelpers.OnRoadLimit(self);
        bool noPassingLane = DriverHelpers.ClearLaneOnSide(p.Lateral, self.Side) is null
            && DriverHelpers.ClearLaneOnSide(p.Lateral, -self.Side) is not null;
        if (offRoad || noPassingLane) self.Side = -self.Side;
    }
}
