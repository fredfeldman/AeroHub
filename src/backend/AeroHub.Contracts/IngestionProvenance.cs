namespace AeroHub.Contracts;

public sealed record IngestionProvenance(
    IngestionPath Path,
    string SourceId,
    string SourceName,
    string? SourceApp,
    string? SourceFormat,
    string AdapterName,
    string AdapterVersion,
    DateTimeOffset? OriginalTimestampUtc,
    DateTimeOffset ReceivedAtUtc,
    string RawReference);