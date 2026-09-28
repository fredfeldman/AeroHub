namespace AeroHub.Contracts;

public sealed record RadioSondeImportRecord(
    string? Serial,
    string? SondeType,
    double? Lat,
    double? Lon,
    double? AltitudeMeters,
    double? AscentRateMetersPerSecond,
    double? TemperatureCelsius,
    double? HumidityPercent,
    double? PressureHpa,
    int? FrameSequence,
    bool? CrcValid,
    bool? BurstKill,
    double? BatteryVoltage,
    double? FrequencyMHz,
    double? Rssi,
    string? SourceApp,
    string? SourceFormat,
    string? SourceVersion,
    DateTimeOffset? OriginalTimestampUtc);
