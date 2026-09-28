namespace AeroHub.Contracts;

public sealed record SpectrumFrame(
    string SourceId,
    long Sequence,
    DateTimeOffset GeneratedAtUtc,
    double CenterFrequencyMHz,
    double SpanKHz,
    IReadOnlyList<float> Bins);