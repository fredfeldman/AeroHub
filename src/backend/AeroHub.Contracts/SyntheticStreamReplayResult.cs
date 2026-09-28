namespace AeroHub.Contracts;

public sealed record SyntheticStreamReplayResult(
    string SourceId,
    int SpeedMultiplier,
    int SpectrumFramesProduced,
    int WaterfallRowsProduced,
    DateTimeOffset ReplayedAtUtc);