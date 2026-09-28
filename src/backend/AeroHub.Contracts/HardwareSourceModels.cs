namespace AeroHub.Contracts;

public sealed record HardwareSourceCapabilities(
    double MinimumSampleRateHz,
    double MaximumSampleRateHz,
    double MaximumBandwidthHz,
    double MinimumFrequencyMHz,
    double MaximumFrequencyMHz,
    IReadOnlyList<string> GainControls,
    bool RequiresExclusiveOwnership,
    string StreamFraming,
    bool AcknowledgesTuneCommands);

public sealed record HardwareSourceMetrics(
    string SourceId,
    SourceState State,
    double ConfiguredSampleRateHz,
    double ConfiguredBandwidthHz,
    double? TunedFrequencyMHz,
    long BuffersReceived,
    long DroppedBuffers,
    int ReconnectCount,
    double? SignalLevelDbfs,
    double? SnrDb,
    int QueueDepth,
    bool IsClipping,
    DateTimeOffset? LastBufferAtUtc);

public sealed record HardwareSourceSnapshot(
    string Id,
    string Name,
    SourceState State,
    string AdapterName,
    string InputKind,
    string? OwnerDecoderId,
    HardwareSourceCapabilities Capabilities,
    HardwareSourceMetrics Metrics,
    string? LastError);

public sealed record HardwareSourceDiagnostic(
    string Id,
    DateTimeOffset OccurredAtUtc,
    string SourceId,
    string Severity,
    string Code,
    string Message);

public sealed record HardwareSourceStartRequest(
    string SourceId,
    string DecoderId,
    double SampleRateHz,
    double BandwidthHz,
    double? FrequencyMHz,
    bool RequiresExclusiveOwnership);

public sealed record HardwareSourceSimulationResult(
    string SourceId,
    string Scenario,
    SourceState State,
    long DroppedBuffers,
    int ReconnectCount,
    bool IsClipping,
    string? LastError);
