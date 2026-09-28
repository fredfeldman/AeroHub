namespace AeroHub.Contracts;

public sealed record WaterfallRows(
    string SourceId,
    long FirstSequence,
    DateTimeOffset GeneratedAtUtc,
    int Width,
    IReadOnlyList<WaterfallRow> Rows);

public sealed record WaterfallRow(
    long Sequence,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<byte> Intensities);