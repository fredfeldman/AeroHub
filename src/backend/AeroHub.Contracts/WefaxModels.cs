namespace AeroHub.Contracts;

public enum WefaxSyncState
{
    Searching,
    Locked,
    Corrected,
    Coasting,
    Lost
}

public sealed record WefaxDecoderState(
    string SourceId,
    SourceState State,
    WefaxSyncState SyncState,
    int Ioc,
    int LineRateRpm,
    bool IsInverted,
    double SlantCorrection,
    int ImageWidth,
    int LinesReceived,
    DateTimeOffset? LastLineAtUtc,
    IReadOnlyList<AviationWarning> Warnings);

public sealed record WefaxToneMetrics(
    double StartToneLevel,
    double StopToneLevel,
    DateTimeOffset MeasuredAtUtc);

public sealed record WefaxImageLine(
    string SourceId,
    long Sequence,
    DateTimeOffset GeneratedAtUtc,
    int LineNumber,
    int Width,
    WefaxSyncState SyncState,
    double Confidence,
    WefaxToneMetrics ToneMetrics,
    IReadOnlyList<byte> Pixels);

public sealed record WefaxLineBatch(
    string SourceId,
    long FirstSequence,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<WefaxImageLine> Lines,
    WefaxDecoderState State);

public sealed record WefaxReplayResult(
    string SourceId,
    int LinesPublished,
    bool IsPartial,
    WefaxSyncState FinalSyncState,
    DateTimeOffset ReplayedAtUtc);