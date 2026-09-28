namespace AeroHub.Contracts;

public sealed record ImportReplayResult(
    string ImportId,
    string SourceFormat,
    int AcceptedRecords,
    int RejectedRecords,
    DateTimeOffset ImportedAtUtc);