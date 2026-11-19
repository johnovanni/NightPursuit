using NightPursuit.Core.Hazards;
using NightPursuit.Core.Police;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

/// <summary>
/// An ordinary road user: keeps its lane at its cruise speed, steers round wrecks, and keeps clear of the player when
/// the player is ahead of it. It never fires or drops anything. It reacts to danger, in priority order: shaken when
/// hit, pulling over to the shoulder in a pursuit, swerving from a fast car behind or the player's gun line, and
/// slowing in smoke.
/// </summary>
public sealed class CivilianDriver : IDriver
{
    public static readonly CivilianDriver Instance = new();

    public enum Mode { Cruise, Swerve, PullOver, Shaken }

    /// <summary>Where a shaken or pulled-over car parks, metres from the road centre (clear of the outer lane).</summary>
    public const double Shoulder = 8.0;
    const double ShakenSeconds = 3, ShakenSpeed = 0.5;
    const double PullOverRange = 150, PullOverSpeed = 12, PullOverReturnSeconds = 3;
    const double SwerveSeconds = 2, SwerveSpeed = 0.7, NoLaneSpeed = 0.6, SmokeSpeed = 0.6;
    const double TimeToContact = 2.5, MinClosing = 3, GunRange = 80, GunHalfWidth = 2.5;
    const double LevelOffset = 0.25;

    public sealed class State
    {
        /// <summary>The lane this car belongs in: its own until a swerve moves it to another.</summary>
        public double Lane { get; set; }
        public AvoidMemory Avoid { get; } = new();
        /// <summary>What this car is doing about the player ahead of it (its own memory, apart from <see cref="AgentCar.AheadMemory"/>).</summary>
        public CarAheadMemory PlayerAhead { get; } = new();
        public Mode Mode { get; set; }
        /// <summary>Shaken and Swerve: seconds left. PullOver: seconds since the last trigger.</summary>
        public double ModeTimer { get; set; }
        /// <summary>-1 or +1: the shoulder a shaken or pulled-over car heads for.</summary>
        public int ShoulderSide { get; set; }
        /// <summary>A swerve with no free lane holds its lane and slows harder.</summary>
        public bool SwerveHeld { get; set; }
    }

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        if (self.DriverState is not State s)
        {
            s = new State { Lane = DriverHelpers.NearestLane(self.Lateral) };
            self.DriverState = s;
        }

        // 1. Shaken: a hit starts it; further hits restart it.
        if (self.HitFlash > 0)
        {
            if (s.Mode != Mode.Shaken) s.ShoulderSide = NearerSide(self);
            s.Mode = Mode.Shaken;
            s.ModeTimer = ShakenSeconds;
        }
        else if (s.Mode == Mode.Shaken)
        {
            s.ModeTimer = DriverHelpers.Countdown(s.ModeTimer, dt);
            if (s.ModeTimer <= 0) s.Mode = Mode.Cruise;
        }
        if (s.Mode == Mode.Shaken) return new DriveIntent(self.CruiseSpeed * ShakenSpeed, s.ShoulderSide * Shoulder);

        // 2. Pull over: a pursuit nearby sends it to the shoulder; it stays until 3 s without one.
        bool pursuit = PursuitNearby(self, view);
        if (pursuit && s.Mode != Mode.PullOver)
        {
            s.Mode = Mode.PullOver;
            s.ShoulderSide = NearerSide(self);
        }
        if (s.Mode == Mode.PullOver)
        {
            s.ModeTimer = pursuit ? 0 : s.ModeTimer + dt;
            if (s.ModeTimer < PullOverReturnSeconds - 1e-9)
                return new DriveIntent(Math.Min(self.CruiseSpeed, PullOverSpeed), s.ShoulderSide * Shoulder);
            s.Mode = Mode.Cruise;
        }

        // 3. Swerve: a fast car closing from behind, or the player's guns firing at it.
        if (s.Mode == Mode.Swerve)
        {
            s.ModeTimer = DriverHelpers.Countdown(s.ModeTimer, dt);
            if (s.ModeTimer <= 0) s.Mode = Mode.Cruise;
        }
        if (s.Mode != Mode.Swerve) StartSwerveIfThreatened(self, view, s);
        if (s.Mode == Mode.Swerve)
            return DriverHelpers.DriveInLane(self, view, s.Lane, s.Avoid, s.PlayerAhead, dt, s.SwerveHeld ? NoLaneSpeed : SwerveSpeed);

        // 4. Smoke: slow, keep the lane. 5. Otherwise plain driving.
        return DriverHelpers.DriveInLane(self, view, s.Lane, s.Avoid, s.PlayerAhead, dt, InSmoke(self, view) ? SmokeSpeed : 1);
    }

    static int NearerSide(AgentCar self) => self.Lateral < 0 ? -1 : self.Lateral > 0 ? 1 : self.Side;

    static bool InSmoke(AgentCar self, WorldView view)
    {
        foreach (var h in view.Hazards)
            if (h is SmokeCloud c && c.Covers(self)) return true;
        return false;
    }

    /// <summary>Heat at Pursuit or above, and the player or a chasing (non-patrol) police car within 150 m.</summary>
    static bool PursuitNearby(AgentCar self, WorldView view)
    {
        if (view.HeatLevel < HeatLevel.Pursuit) return false;
        if (Math.Abs(view.Player.Distance - self.Distance) <= PullOverRange) return true;
        foreach (var other in view.Agents)
            if (other.Faction == Faction.Police && other.Driver is not PatrolDriver
                && Math.Abs(other.Distance - self.Distance) <= PullOverRange) return true;
        return false;
    }

    static void StartSwerveIfThreatened(AgentCar self, WorldView view, State s)
    {
        Vehicle? threat = null;
        double closing = 0, best = double.MaxValue;

        void Consider(Vehicle other)
        {
            if (other.Faction == Faction.Civilian || !other.IsOnRoadDriving || other.Distance >= self.Distance) return;
            if (Math.Abs(other.Lateral - self.Lateral) >= (self.Stats.Width + other.Stats.Width) / 2 + 0.3) return;
            double c = other.Speed - self.Speed;
            if (c <= MinClosing) return;
            double gap = self.Distance - other.Distance - (self.Stats.Length + other.Stats.Length) / 2;
            double ttc = Math.Max(0, gap) / c;
            if (ttc > TimeToContact || ttc >= best) return;
            threat = other;
            closing = c;
            best = ttc;
        }

        Consider(view.Player);
        foreach (var other in view.Agents)
            if (!ReferenceEquals(other, self)) Consider(other);

        if (threat is null && view.PlayerFiring)
        {
            var p = view.Player;
            double ahead = self.Distance - p.Distance;
            if (ahead >= 0 && ahead <= GunRange && Math.Abs(self.Lateral - p.Lateral) < GunHalfWidth)
            {
                threat = p;
                closing = Math.Max(0, p.Speed - self.Speed);
            }
        }
        if (threat is null) return;

        double offset = self.Lateral - threat.Lateral;
        int prefer = Math.Abs(offset) >= LevelOffset ? Math.Sign(offset) : NearerSide(self);
        double? lane = DriverHelpers.FreeNeighbourLane(self, view, Math.Max(15, closing * TimeToContact), 40, prefer);
        s.Mode = Mode.Swerve;
        s.ModeTimer = SwerveSeconds;
        s.SwerveHeld = lane is null;
        if (lane is double l) s.Lane = l;
    }
}
