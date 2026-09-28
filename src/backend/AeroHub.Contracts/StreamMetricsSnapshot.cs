namespace AeroHub.Contracts;

public sealed record StreamMetricsSnapshot(
    string SourceId,
    SourceState State,
    int SpeedMultiplier,
    long SpectrumFramesProduced,
    long WaterfallRowsProduced,
    long DroppedVisualizationFrames,
    int QueueDepth,
    int SlowClientCount,
    DateTimeOffset? LastGeneratedAtUtc);