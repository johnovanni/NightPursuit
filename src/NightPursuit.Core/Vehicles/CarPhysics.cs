using NightPursuit.Core.Control;
using NightPursuit.Core.Road;

namespace NightPursuit.Core.Vehicles;

/// <summary>The player's speed model and turn-to-aim steering.</summary>
public static class CarPhysics
{
    public static void StepPlayer(PlayerCar car, PedalOutput pedals, double steer, Track track, FeelSettings f, double dt)
    {
        car.Throttle = pedals.Throttle;
        car.Brake = pedals.Brake;
        car.Steer = steer;

        double v = car.Speed, rr = v / f.MaxSpeed;
        double engine = car.Throttle * f.MaxAccel * (1 - rr * rr);
        double coast = v > 0.1 ? 0.6 + 1.6 * (1 - car.Throttle) * rr : 0;
        double brake = car.Brake * f.BrakeDecel;
        double gravity = -9.8 * track.SlopeAt(car.Distance) * 0.6;
        car.OffRoad = Math.Abs(car.Lateral) > Track.RoadHalfWidth + 0.4;
        double offRoad = car.OffRoad ? 0.35 * v + 2 : 0;

        v = Math.Max(0, v + (engine - coast - brake + gravity - offRoad) * dt);
        car.Speed = v;

        // Steering turns the nose; the road turning under the car swings it outward on bends;
        // the assist straightens it toward the road.
        double turn = car.Steer * f.TurnRateAt(v) - track.CurvatureAt(car.Distance) * v;
        if (f.AssistWhileSteering || Math.Abs(car.Steer) < 0.05) turn -= car.Heading * f.Straighten;
        double maxHeading = f.MaxHeadingAt(v);
        car.Heading = Math.Clamp(car.Heading + turn * dt, -maxHeading, maxHeading);
        car.LateralVel = v * Math.Sin(car.Heading) + car.PushVel;
        car.Lateral += car.LateralVel * dt;
        car.PushVel *= Math.Exp(-5 * dt);

        if (Math.Abs(car.Lateral) > Track.GuardRail)
        {
            car.Lateral = Math.Sign(car.Lateral) * Track.GuardRail;
            car.Heading *= 0.5;
            car.Speed *= Math.Exp(-1.5 * dt);
            car.Armor -= 6 * dt;
            car.Shake = Math.Max(car.Shake, 0.3);
        }
        if (car.OffRoad) car.Shake = Math.Max(car.Shake, 0.12);
        car.Shake = Math.Max(0, car.Shake - dt);
        car.Distance += v * Math.Cos(car.Heading) * dt;
    }
}
