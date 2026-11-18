namespace NightPursuit.Core.Road;

/// <summary>Parameters of a generated track. Radii and hill amplitude are in metres.</summary>
public sealed record TrackProfile(
    int Seed,
    double MinRadius = 180,
    double MaxRadius = 700,
    double HillAmplitude = 16,
    int Segments = 4000);
