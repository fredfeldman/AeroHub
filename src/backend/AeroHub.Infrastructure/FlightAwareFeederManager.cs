using System.Text;
using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class FlightAwareFeederManager : IFeederManager
{
    private const int MaximumDiagnostics = 200;
    private readonly IClock _clock;
    private readonly ISettingsStore? _settingsStore;
    private readonly object _gate = new();
    private readonly List<FeederDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, FeederStatusSnapshot> _feeders = new(StringComparer.OrdinalIgnoreCase);
    private int _diagnosticSequence;

    public FlightAwareFeederManager(IClock clock, ISettingsStore? settingsStore = null)
    {
        _clock = clock;
        _settingsStore = settingsStore;

        var defaultFeeders = new List<FeederStatusSnapshot>
        {
            new("flightaware-beast-outbound", "FlightAware PiAware / FlightFeeder (Beast Binary)", "FlightAware", FeederProtocol.BeastBinary, FeederMode.OutboundPush, SourceState.Offline, "feed.flightaware.com", 30005, "KDFW-FA-1", 0, 0, 0, 0, null, null),
            new("flightaware-basestation-outbound", "FlightAware BaseStation SBS-1 Feed", "FlightAware", FeederProtocol.BaseStationSbs1, FeederMode.OutboundPush, SourceState.Offline, "feed.flightaware.com", 30003, "KDFW-FA-1", 0, 0, 0, 0, null, null),
            new("flightaware-local-beast-server", "Local Beast Port 30005 Feeder Proxy Server", "FlightAware / PiAware Local Feeder", FeederProtocol.BeastBinary, FeederMode.ServerListener, SourceState.Offline, "0.0.0.0", 30005, "KDFW-LOCAL-1", 0, 0, 0, 0, null, null)
        };

        var savedFeeders = _settingsStore?.GetSettings().Feeders;

        foreach (var def in defaultFeeders)
        {
            var saved = savedFeeders?.FirstOrDefault(f => string.Equals(f.FeederId, def.Id, StringComparison.OrdinalIgnoreCase));
            var state = saved is { AutoStart: true } ? SourceState.Online : SourceState.Offline;
            var host = !string.IsNullOrWhiteSpace(saved?.Host) ? saved.Host : def.Host;
            var port = saved?.Port is >= 1 and <= 65535 ? saved.Port : def.Port;
            var stationId = !string.IsNullOrWhiteSpace(saved?.StationId) ? saved.StationId : def.StationId;

            _feeders[def.Id] = def with
            {
                State = state,
                Host = host,
                Port = port,
                StationId = stationId
            };
        }
    }

    public event EventHandler<FeederDiagnostic>? DiagnosticReceived;

    public event EventHandler<FeederStatusSnapshot>? FeederStateChanged;

    public IReadOnlyList<FeederStatusSnapshot> GetFeeders()
    {
        lock (_gate)
        {
            return _feeders.Values.OrderBy(feeder => feeder.Id).ToArray();
        }
    }

    public IReadOnlyList<FeederDiagnostic> GetDiagnostics(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumDiagnostics);

        lock (_gate)
        {
            return _diagnostics
                .OrderByDescending(diagnostic => diagnostic.OccurredAtUtc)
                .Take(boundedLimit)
                .ToArray();
        }
    }

    public Task<FeederStatusSnapshot> StartFeederAsync(FeederStartRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_feeders.TryGetValue(request.FeederId, out var feeder))
            {
                throw new InvalidOperationException($"Unknown feeder '{request.FeederId}'.");
            }

            var host = string.IsNullOrWhiteSpace(request.Host) ? feeder.Host : request.Host.Trim();
            var port = request.Port is >= 1 and <= 65535 ? request.Port.Value : feeder.Port;
            var stationId = string.IsNullOrWhiteSpace(request.StationId) ? feeder.StationId : request.StationId.Trim();
            var connectedClients = feeder.Mode == FeederMode.ServerListener ? 1 : 0;

            var updated = feeder with
            {
                State = SourceState.Online,
                Host = host,
                Port = port,
                StationId = stationId,
                ConnectedClients = connectedClients,
                LastError = null
            };

            _feeders[request.FeederId] = updated;
            AddDiagnostic(request.FeederId, "Info", "FEEDER_STARTED", $"Started FlightAware data feeder '{updated.Name}' targetting {updated.Host}:{updated.Port} ({updated.Protocol}).");
            PersistFeederSettings();
            FeederStateChanged?.Invoke(this, updated);
            return Task.FromResult(updated);
        }
    }

    public Task<FeederStatusSnapshot> StopFeederAsync(string feederId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_feeders.TryGetValue(feederId, out var feeder))
            {
                throw new InvalidOperationException($"Unknown feeder '{feederId}'.");
            }

            var updated = feeder with
            {
                State = SourceState.Offline,
                ConnectedClients = 0
            };

            _feeders[feederId] = updated;
            AddDiagnostic(feederId, "Info", "FEEDER_STOPPED", $"Stopped FlightAware data feeder '{updated.Name}'.");
            PersistFeederSettings();
            FeederStateChanged?.Invoke(this, updated);
            return Task.FromResult(updated);
        }
    }

    private void PersistFeederSettings()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var current = _settingsStore.GetSettings();
        var feederSettingsList = _feeders.Values.Select(f => new FeederSettings(
            f.Id,
            f.Host ?? "",
            f.Port,
            f.StationId ?? "",
            AutoStart: f.State == SourceState.Online
        )).ToList();

        _settingsStore.UpdateSettings(current with { Feeders = feederSettingsList });
    }

    public Task<FeederStatusSnapshot> SendTestFrameAsync(string feederId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_feeders.TryGetValue(feederId, out var feeder))
            {
                throw new InvalidOperationException($"Unknown feeder '{feederId}'.");
            }

            if (feeder.State != SourceState.Online)
            {
                throw new InvalidOperationException($"Feeder '{feederId}' is offline. Start the feeder before sending frames.");
            }

            byte[] frameBytes = feeder.Protocol switch
            {
                FeederProtocol.BeastBinary => EncodeBeastModeSLongFrame("A1B2C3", [0x8D, 0xA1, 0xB2, 0xC3, 0x20, 0x2C, 0xC3, 0x71, 0xC3, 0x2C, 0xE0, 0x57, 0x60, 0x98], _clock.UtcNow),
                FeederProtocol.BaseStationSbs1 => Encoding.UTF8.GetBytes(FormatBaseStationPositionLine("A1B2C3", "AAL123", 33.1041, -96.7083, 34000, 432.5, 88.2, _clock.UtcNow)),
                FeederProtocol.AvrHex => Encoding.UTF8.GetBytes("*8DA1B2C3202CC371C32CE0576098;\r\n"),
                _ => Encoding.UTF8.GetBytes($"{{\"hex\":\"A1B2C3\",\"station\":\"{feeder.StationId}\",\"now\":{_clock.UtcNow.ToUnixTimeSeconds()}}}\n")
            };

            var updated = feeder with
            {
                FramesSent = feeder.FramesSent + 1,
                BytesSent = feeder.BytesSent + frameBytes.Length,
                LastSentAtUtc = _clock.UtcNow
            };

            _feeders[feederId] = updated;
            AddDiagnostic(feederId, "Info", "FEEDER_TEST_FRAME_SENT", $"Sent test {feeder.Protocol} frame ({frameBytes.Length} bytes) to FlightAware feeder {feeder.Host}:{feeder.Port}.");
            FeederStateChanged?.Invoke(this, updated);
            return Task.FromResult(updated);
        }
    }

    /// <summary>
    /// Encodes a 112-bit Mode S frame into Jetvision Mode-S Beast binary format.
    /// Handles 0x1A escape byte doubling as specified by the Beast protocol.
    /// </summary>
    public static byte[] EncodeBeastModeSLongFrame(string hexAddress, byte[] modeSBytes, DateTimeOffset utcNow)
    {
        var result = new List<byte> { 0x1A, 0x33 }; // 0x1A escape + '3' (Mode S 112-bit long frame)

        // 48-bit 12 MHz clock tick calculation
        var ticks = (long)(utcNow.TimeOfDay.TotalSeconds * 12_000_000) & 0xFFFFFFFFFFFF;
        for (var i = 5; i >= 0; i--)
        {
            var b = (byte)((ticks >> (i * 8)) & 0xFF);
            result.Add(b);
            if (b == 0x1A)
            {
                result.Add(0x1A); // Escape doubling
            }
        }

        // RSSI byte (e.g. 210)
        byte rssi = 210;
        result.Add(rssi);
        if (rssi == 0x1A)
        {
            result.Add(0x1A);
        }

        // 14 bytes Mode S payload
        for (var i = 0; i < Math.Min(14, modeSBytes.Length); i++)
        {
            var b = modeSBytes[i];
            result.Add(b);
            if (b == 0x1A)
            {
                result.Add(0x1A); // Escape doubling
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Formats an airborne position observation as a BaseStation / SBS-1 MSG,3 CSV string.
    /// </summary>
    public static string FormatBaseStationPositionLine(
        string hexAddress,
        string? callsign,
        double lat,
        double lon,
        int? altFeet,
        double? gsKnots,
        double? trackDegrees,
        DateTimeOffset utcNow)
    {
        var dateStr = utcNow.ToString("yyyy/MM/dd");
        var timeStr = utcNow.ToString("HH:mm:ss.fff");
        var flight = callsign?.Trim() ?? "";
        var altStr = altFeet?.ToString() ?? "";
        var gsStr = gsKnots?.ToString("F1") ?? "";
        var trackStr = trackDegrees?.ToString("F1") ?? "";

        return $"MSG,3,1,1,{hexAddress.ToUpperInvariant()},1,{dateStr},{timeStr},{dateStr},{timeStr},{flight},{altStr},{gsStr},{trackStr},{lat:F5},{lon:F5},,,0,0,0,0\r\n";
    }

    private void AddDiagnostic(string feederId, string severity, string code, string message, string? rawReference = null)
    {
        var diagnostic = new FeederDiagnostic(
            $"feeder-diag-{Interlocked.Increment(ref _diagnosticSequence)}",
            _clock.UtcNow,
            feederId,
            severity,
            code,
            message,
            rawReference);

        _diagnostics.Add(diagnostic);

        if (_diagnostics.Count > MaximumDiagnostics)
        {
            _diagnostics.RemoveRange(0, _diagnostics.Count - MaximumDiagnostics);
        }

        DiagnosticReceived?.Invoke(this, diagnostic);
    }
}
