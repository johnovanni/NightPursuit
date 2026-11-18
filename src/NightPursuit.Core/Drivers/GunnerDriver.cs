using NightPursuit.Core.Road;
using NightPursuit.Core.Vehicles;

namespace NightPursuit.Core.Drivers;

public enum GunnerPhase { Overtake, Hold, Aim, Burst }

/// <summary>
/// Gets ahead of the player, holds about 45 m in front, then warns for 0.6 s and fires a 0.45 s burst backwards.
/// During the warning it stays in its lane, so leaving that lane dodges the burst.
/// </summary>
public sealed class GunnerDriver : IDriver
{
    const double OvertakeBelow = 25, HoldFrom = 35;
    const double HoldGapWanted = 45, HoldGain = 0.8, HoldSpeedRange = 10;
    const double HoldSeconds = 2.5, AimSeconds = 0.6, BurstSeconds = 0.45;
    const double AimLateral = 2, AimMin = 25, AimMax = 90, LaneLimit = 5;

    public sealed class State
    {
        public GunnerPhase Phase { get; set; } = GunnerPhase.Overtake;
        public double Timer { get; set; }
        public double Target { get; set; }
        public AvoidMemory Avoid { get; } = new();
    }

    /// <summary>
    /// True while the car is warning (Aim) or firing (Burst) and can see you; the HUD uses it to warn the player. A car
    /// blinded by smoke keeps its plan but is driven by the blind driver, which never fires, so it is not aiming.
    /// </summary>
    public static bool IsAiming(AgentCar c) =>
        c.Blinded <= 0 && c.DriverState is State { Phase: GunnerPhase.Aim or GunnerPhase.Burst };

    public DriveIntent Decide(AgentCar self, WorldView view, double dt)
    {
        if (self.DriverState is not State s)
            self.DriverState = s = new State { Target = self.Lateral };

        var p = view.Player;
        double dD = self.Distance - p.Distance;
        if (dD < OvertakeBelow && s.Phase != GunnerPhase.Overtake) s.Phase = GunnerPhase.Overtake;

        double speed;
        bool fire = false;
        switch (s.Phase)
        {
            case GunnerPhase.Overtake:
                speed = p.Speed + 12;
                s.Target = NearestOtherLane(self, p.Lateral);
                if (dD >= HoldFrom) { s.Phase = GunnerPhase.Hold; s.Timer = HoldSeconds; }
                break;
            case GunnerPhase.Hold:
                speed = HoldSpeed(p.Speed, dD);
                s.Target = Math.Clamp(p.Lateral, -LaneLimit, LaneLimit);
                s.Timer = DriverHelpers.Countdown(s.Timer, dt);
                if (s.Timer <= 0 && view.CanSeePlayer(self) && Math.Abs(self.Lateral - p.Lateral) < AimLateral
                    && dD >= AimMin && dD <= AimMax)
                {
                    s.Phase = GunnerPhase.Aim;
                    s.Timer = AimSeconds;
                    s.Target = self.Lateral;
                }
                break;
            case GunnerPhase.Aim:
                speed = HoldSpeed(p.Speed, dD);
                s.Timer = DriverHelpers.Countdown(s.Timer, dt);
                if (s.Timer <= 0) { s.Phase = GunnerPhase.Burst; s.Timer = BurstSeconds; }
                break;
            default:
                speed = HoldSpeed(p.Speed, dD);
                fire = true;
                s.Timer = DriverHelpers.Countdown(s.Timer, dt);
                if (s.Timer <= 0) { s.Phase = GunnerPhase.Hold; s.Timer = HoldSeconds; }
                break;
        }

        double lateral = DriverHelpers.SteerAroundWrecks(self, view, s.Target, s.Avoid, DriverHelpers.ReactionDelay(self.Kind), dt);
        return new DriveIntent(speed, lateral, fire);
    }

    static double HoldSpeed(double playerSpeed, double dD) =>
        DriverHelpers.HoldGap(playerSpeed, dD, HoldGapWanted, HoldGain, -HoldSpeedRange, HoldSpeedRange);

    /// <summary>The lane centre nearest the car that is not the lane the player is in.</summary>
    static double NearestOtherLane(AgentCar self, double playerLateral)
    {
        int playerLane = 0;
        for (int i = 1; i < Track.LaneCentres.Length; i++)
            if (Math.Abs(Track.LaneCentres[i] - playerLateral) < Math.Abs(Track.LaneCentres[playerLane] - playerLateral)) playerLane = i;
        double best = double.NaN;
        for (int i = 0; i < Track.LaneCentres.Length; i++)
        {
            if (i == playerLane) continue;
            double lane = Track.LaneCentres[i];
            if (double.IsNaN(best) || Math.Abs(lane - self.Lateral) < Math.Abs(best - self.Lateral)) best = lane;
        }
        return best;
    }
}
