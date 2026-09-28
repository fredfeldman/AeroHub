namespace AeroHub.Contracts;

public sealed record DecodedImportRecord(
    string? RawPayload,
    AviationMessageKind? Kind,
    string? SourceApp,
    string? SourceFormat,
    string? SourceVersion,
    DateTimeOffset? OriginalTimestampUtc,
    string? Transport,
    double? FrequencyMHz);