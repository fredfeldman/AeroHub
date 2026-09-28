namespace AeroHub.Contracts;

public sealed record ExternalDecoderProcessSnapshot(
    string Id,
    string Name,
    SourceState State,
    string ExecutableName,
    string? DetectedVersion,
    int RestartCount,
    int RestartLimit,
    DateTimeOffset? LastStartedAtUtc,
    DateTimeOffset? LastStoppedAtUtc,
    string? LastError);