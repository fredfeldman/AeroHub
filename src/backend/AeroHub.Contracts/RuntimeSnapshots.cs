namespace AeroHub.Contracts;

public sealed record SignalSourceSnapshot(
    string Id,
    string Name,
    SourceState State,
    string AdapterName,
    double? FrequencyMHz,
    IReadOnlyList<string> Capabilities);

public sealed record DecodedDataSourceSnapshot(
    string Id,
    string Name,
    SourceState State,
    string AdapterName,
    string SourceFormat,
    long AcceptedRecords,
    long RejectedRecords);

public sealed record DecoderStateSnapshot(
    string Id,
    string Name,
    SourceState State,
    IReadOnlyList<AviationMessageKind> Produces,
    bool RequiresExclusiveSource);

public sealed record ImportStateSnapshot(
    string Id,
    string Name,
    SourceState State,
    string SourceFormat,
    long AcceptedRecords,
    long RejectedRecords,
    string? LastError);

public sealed record AircraftTrackSnapshot(
    string AircraftIdentifier,
    DateTimeOffset UpdatedAtUtc,
    double? Latitude,
    double? Longitude,
    double? AltitudeFeet,
    string SourceId,
    ParserConfidence Confidence,
    string? Callsign,
    double? GroundSpeedKnots,
    double? TrackDegrees,
    string SourceType,
    string CorrelationGroupId,
    bool IsStale,
    IngestionProvenance Provenance,
    string? EmitterCategory = null,
    string? AircraftType = null,
    string? Registration = null,
    string? OperatorName = null,
    bool IsMilitary = false,
    string? RemoteIdSerialNumber = null,
    string? RemoteIdOperatorId = null,
    string? RemoteIdOperationType = null);

public sealed record AircraftPositionSample(
    string AircraftIdentifier,
    DateTimeOffset RecordedAtUtc,
    double Latitude,
    double Longitude,
    double? AltitudeFeet,
    double? GroundSpeedKnots,
    double? TrackDegrees);

public sealed record SondeTelemetrySnapshot(
    string Serial,
    DateTimeOffset UpdatedAtUtc,
    double? Latitude,
    double? Longitude,
    double? AltitudeMeters,
    string SourceId,
    ParserConfidence Confidence,
    string? SondeType,
    double? AscentRateMetersPerSecond,
    double? TemperatureCelsius,
    double? HumidityPercent,
    double? PressureHpa,
    string SourceType,
    string CorrelationGroupId,
    bool IsStale,
    IngestionProvenance Provenance,
    int? FrameSequence = null,
    bool CrcValid = true,
    bool BurstKill = false,
    double? BatteryVoltage = null,
    double? FrequencyMHz = null,
    double? Rssi = null);

public sealed record SondePositionSample(
    string Serial,
    DateTimeOffset RecordedAtUtc,
    double Latitude,
    double Longitude,
    double? AltitudeMeters,
    double? AscentRateMetersPerSecond);