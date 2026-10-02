namespace AeroHub.Contracts;

public sealed record RemoteIdDroneImportRecord(
    string? SerialNumber,
    string? OperatorId,
    string? OperationType,
    double? Latitude,
    double? Longitude,
    double? AltitudeMeters,
    double? SpeedMetersPerSecond,
    double? DirectionDegrees,
    double? SeenSeconds,
    string? SourceApp,
    string? SourceFormat,
    DateTimeOffset? OriginalTimestampUtc);