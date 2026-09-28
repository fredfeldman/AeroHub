using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroHub.Contracts;

public sealed record Dump1090ConnectionRequest(
    string Host,
    int Port = 8080,
    string JsonPath = "/data/aircraft.json",
    int PollIntervalSeconds = 5);

public sealed record Dump1090ConnectionSnapshot(
    string ImportId,
    SourceState State,
    string? Host,
    int? Port,
    string? JsonPath,
    int PollIntervalSeconds,
    long AcceptedRecords,
    long RejectedRecords,
    DateTimeOffset? LastPolledAtUtc,
    string? LastError);

public sealed record Dump1090AircraftJsonEntry(
    string? Hex,
    string? Flight,
    double? Lat,
    double? Lon,
    [property: JsonPropertyName("alt_baro")] JsonElement? AltBaroRaw,
    double? Gs,
    double? Track,
    [property: JsonPropertyName("seen_pos")] double? SeenPos,
    long? Messages,
    double? Rssi,
    string? Category,
    [property: JsonPropertyName("t")] string? AircraftType,
    [property: JsonPropertyName("r")] string? Registration);

public sealed record Dump1090AircraftJsonResponse(
    double? Now,
    long? Messages,
    IReadOnlyList<Dump1090AircraftJsonEntry>? Aircraft);
