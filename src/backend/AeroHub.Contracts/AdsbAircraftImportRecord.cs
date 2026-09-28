namespace AeroHub.Contracts;

public sealed record AdsbAircraftImportRecord(
    string? Hex,
    string? Flight,
    double? Lat,
    double? Lon,
    int? AltBaro,
    double? Gs,
    double? Track,
    double? SeenPos,
    int? Messages,
    double? Rssi,
    string? SourceApp,
    string? SourceFormat,
    string? SourceVersion,
    DateTimeOffset? OriginalTimestampUtc,
    string? Category = null,
    string? AircraftType = null,
    string? Registration = null);