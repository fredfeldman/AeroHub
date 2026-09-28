namespace AeroHub.Contracts;

public sealed record NormalizedAviationMessage(
    string Id,
    long Sequence,
    DateTimeOffset ReceivedAtUtc,
    AviationMessageKind Kind,
    string Summary,
    AcarsMessageDetails? Acars,
    string? AircraftIdentifier,
    string? Transport,
    double? FrequencyMHz,
    ParserConfidence Confidence,
    IngestionProvenance Provenance,
    string RawPayload,
    IReadOnlyList<AviationWarning> Warnings,
    DatalinkMessageDetails? Datalink = null,
    SatcomTransportMetadata? Satcom = null);

public sealed record AcarsMessageDetails(
    string? Label,
    string? Sublabel,
    string? Preamble,
    string ApplicationCategory,
    string DecodedText,
    string? UnconsumedText);

public sealed record DatalinkMessageDetails(
    string ApplicationType,
    string Direction,
    string? MessageReference,
    string? AcknowledgementState,
    string Category,
    string? AircraftIdentifier,
    double? Latitude,
    double? Longitude,
    double? AltitudeFeet,
    string RawText);

public sealed record SatcomTransportMetadata(
    string? Satellite,
    string? Channel,
    string? Bearer,
    string? GroundEndpoint,
    string ReassemblyState);