using AeroHub.Contracts;

namespace AeroHub.Decoders.Acars;

public sealed record AcarsParserInput(
    string RawPayload,
    long Sequence,
    DateTimeOffset ReceivedAtUtc,
    IngestionProvenance Provenance,
    double? FrequencyMHz,
    string Transport);