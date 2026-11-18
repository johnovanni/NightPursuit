using NightPursuit.Core.Common;
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

    /// <summary>Moves an agent toward its intent along the road; sideways shoves in PushVel add to its steering.</summary>
    public static void StepAgent(Vehicle car, DriveIntent intent, double dt)
    {
        double target = Math.Min(intent.TargetSpeed, car.Stats.TopSpeed);
        car.Speed = MathX.MoveToward(car.Speed, target, (car.Speed < target ? car.Stats.Accel : 10) * dt);
        double steer = Math.Clamp((intent.TargetLateral - car.Lateral) * 1.8, -car.Stats.SideSpeed, car.Stats.SideSpeed);
        car.LateralVel = steer + car.PushVel;
        car.PushVel *= Math.Exp(-1.2 * dt);
        car.Lateral += car.LateralVel * dt;
        car.Heading = Math.Atan2(car.LateralVel, Math.Max(car.Speed, 1));
        car.Distance += car.Speed * dt;
    }

    /// <summary>A spun-out car: it turns, slides along its shove and loses speed, then drives again.</summary>
    public static void StepSpinning(Vehicle car, double dt)
    {
        car.StateTimer -= dt;
        car.SpinAngle += car.SpinRate * dt;
        car.Speed *= Math.Exp(-0.9 * dt);
        car.LateralVel = car.PushVel;
        car.Lateral += car.PushVel * dt;
        car.Distance += car.Speed * dt;
        if (car.StateTimer <= 0)
        {
            car.State = VehicleState.Driving;
            car.SpinAngle = 0;
            car.PushVel = 0;
        }
    }

    /// <summary>A wrecked car rolls to a stop at 25 m/s^2 while its shove fades.</summary>
    public static void StepWrecked(Vehicle car, double dt)
    {
        car.StateTimer -= dt;
        car.Speed = Math.Max(0, car.Speed - 25 * dt);
        car.Distance += car.Speed * dt;
        car.Lateral += car.PushVel * dt;
        car.PushVel *= Math.Exp(-2 * dt);
    }
}
