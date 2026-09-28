using System.Text.Json.Serialization;

namespace AeroHub.Contracts;

public enum FeederProtocol
{
    BeastBinary,
    AvrHex,
    BaseStationSbs1,
    FlightAwareJson,
    AirframesNdjson
}

public enum FeederMode
{
    OutboundPush,
    ServerListener
}

public sealed record FeederStatusSnapshot(
    string Id,
    string Name,
    string TargetService,
    FeederProtocol Protocol,
    FeederMode Mode,
    SourceState State,
    string? Host,
    int Port,
    string? StationId,
    int ConnectedClients,
    long FramesSent,
    long BytesSent,
    long FramesDropped,
    DateTimeOffset? LastSentAtUtc,
    string? LastError);

public sealed record FeederStartRequest(
    string FeederId,
    string? Host = null,
    int? Port = null,
    string? StationId = null);

public sealed record FeederDiagnostic(
    string Id,
    DateTimeOffset OccurredAtUtc,
    string FeederId,
    string Severity,
    string Code,
    string Message,
    string? RawReference = null);
