namespace AeroHub.Contracts;

public sealed record StorageRetentionPolicy(
    int MessageRetentionDays,
    int TrackRetentionDays,
    int DiagnosticRetentionDays);

public sealed record StorageSnapshot(
    string RootPath,
    long MessageCount,
    long AircraftTrackCount,
    long ImportDiagnosticCount,
    long BytesUsed,
    DateTimeOffset CheckedAtUtc,
    long PositionSampleCount = 0);

public sealed record StorageRetentionResult(
    int MessagesRemoved,
    int TracksRemoved,
    int DiagnosticsRemoved,
    DateTimeOffset AppliedAtUtc);

public sealed record StorageExportResult(
    string ExportPath,
    int MessagesExported,
    int TracksExported,
    int DiagnosticsExported,
    DateTimeOffset ExportedAtUtc);