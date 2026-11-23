namespace NightPursuit.Core.Vehicles;

/// <summary>
/// What an agent's driver wants this step: a speed and a lane position, plus weapon requests. <paramref name="BrakeRate"/>
/// overrides the usual 10 m/s^2 of braking (0 keeps it): the boss's hard stop before it backs into you.
/// </summary>
public readonly record struct DriveIntent(double TargetSpeed, double TargetLateral, bool Fire = false, bool DropRear = false,
    bool FireMissile = false, double BrakeRate = 0);
