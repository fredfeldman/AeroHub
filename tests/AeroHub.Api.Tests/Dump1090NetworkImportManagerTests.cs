using System.Net;
using System.Net.Http;
using AeroHub.Contracts;
using AeroHub.Core;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class Dump1090NetworkImportManagerTests
{
    [Fact]
    public async Task Connect_polls_aircraft_json_and_upserts_valid_tracks()
    {
        const string payload = """
        {
          "now": 1700000000.0,
          "messages": 12345,
          "aircraft": [
            { "hex": "a1b2c3", "flight": "AAL123 ", "lat": 44.881, "lon": -93.221, "alt_baro": 34000, "gs": 432.5, "track": 88.2, "seen_pos": 1.2, "messages": 500, "rssi": -12.3 },
            { "hex": "bad", "lat": 10, "lon": 10 },
            { "hex": "d4e5f6", "lat": 999, "lon": -93.1 }
          ]
        }
        """;

        var clock = new TestClock();
        var trackStore = new InMemoryAircraftTrackStore(clock);
        var handler = new StubHttpMessageHandler(payload);
        var factory = new StubHttpClientFactory(handler);
        var manager = new Dump1090NetworkImportManager(factory, clock, trackStore, new NullAircraftRegistryLookup());

        var snapshot = await manager.ConnectAsync(new Dump1090ConnectionRequest("192.168.50.146", 8080, "/data/aircraft.json", 3600));

        // Give the background poll loop a moment to run its first cycle.
        for (var i = 0; i < 50 && manager.GetStatus().LastPolledAtUtc is null; i++)
        {
            await Task.Delay(20);
        }

        var status = manager.GetStatus();
        var diagnostics = manager.GetDiagnostics(20);
        var tracks = trackStore.GetCurrentTracks();

        Assert.Equal(SourceState.Online, status.State);
        Assert.Equal(1, status.AcceptedRecords);
        Assert.Equal(2, status.RejectedRecords);
        Assert.Contains(tracks, track => track.AircraftIdentifier == "A1B2C3" && track.Callsign == "AAL123" && track.SourceType == "Imported ADS-B/dump1090 (network)");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_SCHEMA_MISMATCH");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_ADSB_INVALID_POSITION");

        await manager.DisconnectAsync();
        Assert.Equal(SourceState.Offline, manager.GetStatus().State);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class NullAircraftRegistryLookup : IAircraftRegistryLookup
    {
        public bool TryLookup(string hexAddress, out AircraftRegistryEntry entry)
        {
            entry = default;
            return false;
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(string jsonPayload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
