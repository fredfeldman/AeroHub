namespace AeroHub.Contracts;

public sealed record NavaidSnapshot(
    string Identifier,
    string Name,
    NavaidType Type,
    double Latitude,
    double Longitude,
    double? ElevationFeet,
    double FrequencyMHz,
    string? AssociatedAirport,
    double DistanceMilesFromCenter,
    double BearingDegreesFromCenter,
    string? UsageNotes = null);
