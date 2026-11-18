namespace NightPursuit.Core.Vehicles;

/// <summary>What an agent's driver wants this step: a speed and a lane position, plus weapon requests.</summary>
public readonly record struct DriveIntent(double TargetSpeed, double TargetLateral, bool Fire = false, bool DropRear = false);
