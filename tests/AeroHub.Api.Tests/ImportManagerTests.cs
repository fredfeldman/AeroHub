using AeroHub.Contracts;
using AeroHub.Core;
using AeroHub.Decoders.Acars;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class ImportManagerTests
{
    [Fact]
    public async Task StartImportAsync_publishes_valid_records_and_quarantines_bad_records()
    {
        var messageBus = new InMemoryMessageBus();
        var importManager = new FileNdjsonImportManager(messageBus, new TestClock(), new AcarsMessageParser(), new InMemoryAircraftTrackStore(new TestClock()), new NullAircraftRegistryLookup());

        var result = await importManager.StartImportAsync("local-acars-ndjson");
        var messages = messageBus.GetRecent(10);
        var diagnostics = importManager.GetDiagnostics(10);
        var snapshot = importManager.GetImports().Single(importState => importState.Id == "local-acars-ndjson");

        Assert.Equal(2, result.AcceptedRecords);
        Assert.Equal(3, result.RejectedRecords);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.Equal(IngestionPath.ImportedDecodedData, message.Provenance.Path));
        Assert.Contains(messages, message => message.Confidence == ParserConfidence.Unknown && message.Warnings.Any(warning => warning.Code == "ACARS_UNKNOWN_LABEL"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_SCHEMA_MISMATCH");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_DUPLICATE_RECORD");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_OUT_OF_ORDER");
        Assert.Equal(SourceState.Online, snapshot.State);
        Assert.Equal(2, snapshot.AcceptedRecords);
        Assert.Equal(3, snapshot.RejectedRecords);
        Assert.Equal("3 record(s) quarantined", snapshot.LastError);
    }

    [Fact]
    public async Task StartImportAsync_creates_adsb_tracks_and_quarantines_bad_aircraft_records()
    {
        var clock = new TestClock();
        var messageBus = new InMemoryMessageBus();
        var trackStore = new InMemoryAircraftTrackStore(clock);
        var importManager = new FileNdjsonImportManager(messageBus, clock, new AcarsMessageParser(), trackStore, new NullAircraftRegistryLookup());

        var result = await importManager.StartImportAsync("local-adsb-readsb-json");
        var tracks = trackStore.GetCurrentTracks();
        var diagnostics = importManager.GetDiagnostics(20);
        var importSnapshot = importManager.GetImports().Single(importState => importState.Id == "local-adsb-readsb-json");

        Assert.Equal(2, result.AcceptedRecords);
        Assert.Equal(3, result.RejectedRecords);
        Assert.Equal(2, tracks.Count);
        Assert.Contains(tracks, track => track.AircraftIdentifier == "A1B2C3" && track.Callsign == "AAL123" && track.SourceType == "Imported ADS-B/readsb");
        Assert.Contains(tracks, track => track.AircraftIdentifier == "A4D5E6" && track.Latitude == 44.942);
        Assert.All(tracks, track => Assert.Equal(IngestionPath.ImportedDecodedData, track.Provenance.Path));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_DUPLICATE_RECORD");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_ADSB_INVALID_POSITION");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_ADSB_STALE");
        Assert.Equal(SourceState.Online, importSnapshot.State);
        Assert.Equal("3 record(s) quarantined", importSnapshot.LastError);
    }

    [Fact]
    public async Task StartImportAsync_creates_remote_id_drone_tracks_and_quarantines_bad_records()
    {
        var clock = new TestClock();
        var trackStore = new InMemoryAircraftTrackStore(clock);
        var importManager = new FileNdjsonImportManager(new InMemoryMessageBus(), clock, new AcarsMessageParser(), trackStore, new NullAircraftRegistryLookup());

        var result = await importManager.StartImportAsync("local-remote-id-json");
        var tracks = trackStore.GetCurrentTracks();
        var diagnostics = importManager.GetDiagnostics(20);

        Assert.Equal(2, result.AcceptedRecords);
        Assert.Equal(3, result.RejectedRecords);
        Assert.Equal(2, tracks.Count);
        var drone = Assert.Single(tracks, track => track.RemoteIdSerialNumber == "RID-COM-1042");
        Assert.Equal("RID:RID-COM-1042", drone.AircraftIdentifier);
        Assert.Equal("OPERATOR-7K4M", drone.RemoteIdOperatorId);
        Assert.Equal("Commercial", drone.RemoteIdOperationType);
        Assert.Equal("Remote ID drone", drone.SourceType);
        Assert.Equal(118.4 * 3.28084, drone.AltitudeFeet!.Value, precision: 3);
        Assert.Equal(8.2 * 1.943844, drone.GroundSpeedKnots!.Value, precision: 3);
        Assert.All(tracks, track => Assert.Equal(IngestionPath.ImportedDecodedData, track.Provenance.Path));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_DUPLICATE_RECORD");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_REMOTE_ID_INVALID_POSITION");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_SCHEMA_MISMATCH");
    }

    [Fact]
    public async Task StartImportAsync_creates_sonde_tracks_and_quarantines_bad_sonde_records()
    {
        var clock = new TestClock();
        var messageBus = new InMemoryMessageBus();
        var trackStore = new InMemoryAircraftTrackStore(clock);
        var sondeTrackStore = new InMemorySondeTrackStore(clock);
        var importManager = new FileNdjsonImportManager(messageBus, clock, new AcarsMessageParser(), trackStore, new NullAircraftRegistryLookup(), sondeTrackStore: sondeTrackStore);

        var result = await importManager.StartImportAsync("local-radiosonde-json");
        var sondes = sondeTrackStore.GetCurrentTracks();
        var diagnostics = importManager.GetDiagnostics(20);
        var importSnapshot = importManager.GetImports().Single(importState => importState.Id == "local-radiosonde-json");

        Assert.Equal(3, result.AcceptedRecords);
        Assert.Equal(3, result.RejectedRecords);
        Assert.Equal(2, sondes.Count);
        Assert.Contains(sondes, sonde => sonde.Serial == "S1720982" && sonde.SondeType == "RS41-SG" && sonde.TemperatureCelsius == 9.8);
        Assert.Contains(sondes, sonde => sonde.Serial == "S1799001" && sonde.BurstKill);
        Assert.All(sondes, sonde => Assert.Equal(IngestionPath.ImportedDecodedData, sonde.Provenance.Path));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_DUPLICATE_RECORD");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_RADIOSONDE_INVALID_POSITION");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_SCHEMA_MISMATCH");
        Assert.Equal(SourceState.Online, importSnapshot.State);
        Assert.Equal("3 record(s) quarantined", importSnapshot.LastError);
    }

    [Fact]
    public void TrackStore_expires_stale_tracks()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        var trackStore = new InMemoryAircraftTrackStore(clock);
        var track = new AircraftTrackSnapshot(
            "A1B2C3",
            clock.UtcNow,
            44.881,
            -93.221,
            34000,
            "test-source",
            ParserConfidence.Observed,
            "AAL123",
            432.5,
            88.2,
            "Imported ADS-B/readsb",
            "adsb-A1B2C3",
            false,
            new IngestionProvenance(
                IngestionPath.ImportedDecodedData,
                "test-source",
                "Test ADS-B source",
                "readsb",
                "application/x-ndjson; domain=adsb-readsb",
                "ImportManagerTests",
                "0.1.0",
                clock.UtcNow,
                clock.UtcNow,
                "fixtures/imports/adsb/test.ndjson:1"));

        Assert.True(trackStore.Upsert(track));
        Assert.Single(trackStore.GetCurrentTracks());

        clock.UtcNow = clock.UtcNow.AddMinutes(16);

        Assert.Empty(trackStore.GetCurrentTracks());
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

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}