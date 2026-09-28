namespace AeroHub.Contracts;

public sealed record HealthStatus(
    string ServiceName,
    string Version,
    string Environment,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<string> Capabilities);

public sealed record OperatorDiagnosticsSnapshot(
    string ServiceName,
    string Version,
    string Environment,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CheckedAtUtc,
    int ActiveDecoders,
    int ActiveImports,
    int StreamQueueDepth,
    int ActiveWarnings,
    string SystemHealth,
    long StorageBytesUsed,
    IReadOnlyList<string> RecentWarnings);