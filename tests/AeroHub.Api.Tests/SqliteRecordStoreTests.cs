using AeroHub.Contracts;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class SqliteRecordStoreTests
{
    [Fact]
    public void SaveMessage_deduplicates_by_id_and_reloads_from_disk()
    {
        using var testDirectory = new TemporaryDirectory();
        var dbPath = Path.Combine(testDirectory.Path, "aerohub.db");

        using (var store = new SqliteRecordStore(dbPath))
        {
            var message = CreateMessage("message-1", DateTimeOffset.UnixEpoch);

            store.SaveMessage(message);
            store.SaveMessage(message);
        }

        using var reloaded = new SqliteRecordStore(dbPath);
        var messages = reloaded.GetRecentMessages(10);

        var saved = Assert.Single(messages);
        Assert.Equal("message-1", saved.Id);
        Assert.Equal(IngestionPath.FixtureReplay, saved.Provenance.Path);
    }

    [Fact]
    public void SaveTrack_keeps_latest_track_and_reload_preserves_provenance()
    {
        using var testDirectory = new TemporaryDirectory();
        var dbPath = Path.Combine(testDirectory.Path, "aerohub.db");
        var oldTrack = CreateTrack(DateTimeOffset.UnixEpoch);
        var newTrack = oldTrack with { UpdatedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1), AltitudeFeet = 34000 };

        using (var store = new SqliteRecordStore(dbPath))
        {
            Assert.True(store.SaveTrack(oldTrack));
            Assert.True(store.SaveTrack(newTrack));
            Assert.False(store.SaveTrack(oldTrack));
        }

        using var reloaded = new SqliteRecordStore(dbPath);
        var track = Assert.Single(reloaded.GetCurrentTracks(DateTimeOffset.UnixEpoch.AddMinutes(2), TimeSpan.FromMinutes(15)));
        Assert.Equal(34000, track.AltitudeFeet);
        Assert.Equal("JsonlRecordStoreTests", track.Provenance.AdapterName);
    }

    [Fact]
    public void SavePosition_caches_history_in_order_and_caps_per_aircraft()
    {
        using var testDirectory = new TemporaryDirectory();
        using var store = new SqliteRecordStore(Path.Combine(testDirectory.Path, "aerohub.db"));

        for (var i = 0; i < 505; i++)
        {
            store.SavePosition(new AircraftPositionSample(
                "N123AB",
                DateTimeOffset.UnixEpoch.AddSeconds(i),
                44.0 + i * 0.001,
                -93.0,
                30000,
                420,
                90));
        }

        var history = store.GetPositionHistory("N123AB", 500);

        Assert.Equal(500, history.Count);
        Assert.True(history[0].RecordedAtUtc < history[^1].RecordedAtUtc);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(504), history[^1].RecordedAtUtc);
    }

    [Fact]
    public void ApplyRetention_removes_stale_position_samples()
    {
        using var testDirectory = new TemporaryDirectory();
        using var store = new SqliteRecordStore(Path.Combine(testDirectory.Path, "aerohub.db"));

        store.SavePosition(new AircraftPositionSample("N123AB", DateTimeOffset.UnixEpoch, 44.0, -93.0, 30000, 420, 90));
        store.SavePosition(new AircraftPositionSample("N123AB", DateTimeOffset.UnixEpoch.AddDays(40), 44.1, -93.1, 30500, 425, 91));

        store.ApplyRetention(new StorageRetentionPolicy(30, 30, 14), DateTimeOffset.UnixEpoch.AddDays(41));

        var history = store.GetPositionHistory("N123AB", 500);
        var sample = Assert.Single(history);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(40), sample.RecordedAtUtc);
    }

    [Fact]
    public void SaveSondeTrack_and_SaveSondePosition_work_and_reload()
    {
        using var testDirectory = new TemporaryDirectory();
        var dbPath = Path.Combine(testDirectory.Path, "aerohub.db");

        var sonde = new SondeTelemetrySnapshot(
            "S1720982",
            DateTimeOffset.UnixEpoch,
            33.2081,
            -96.6153,
            1180.0,
            "test-source",
            ParserConfidence.Observed,
            "RS41-SG",
            5.2,
            21.4,
            48.5,
            889.1,
            "Imported radiosonde telemetry",
            "radiosonde-S1720982",
            false,
            new IngestionProvenance(
                IngestionPath.ImportedDecodedData,
                "test-source",
                "Test RadioSonde Source",
                "rs41mod",
                "application/x-ndjson; domain=radiosonde",
                "SqliteRecordStoreTests",
                "1.0.0",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                "test:1"));

        using (var store = new SqliteRecordStore(dbPath))
        {
            Assert.True(store.SaveSondeTrack(sonde));
            store.SaveSondePosition(new SondePositionSample("S1720982", DateTimeOffset.UnixEpoch, 33.2081, -96.6153, 1180.0, 5.2));
        }

        using var reloaded = new SqliteRecordStore(dbPath);
        var reloadedTrack = Assert.Single(reloaded.GetCurrentSondeTracks(DateTimeOffset.UnixEpoch.AddMinutes(1), TimeSpan.FromMinutes(15)));
        var history = reloaded.GetSondePositionHistory("S1720982", 10);

        Assert.Equal("S1720982", reloadedTrack.Serial);
        Assert.Equal("RS41-SG", reloadedTrack.SondeType);
        Assert.Equal(21.4, reloadedTrack.TemperatureCelsius);
        Assert.Single(history);
        Assert.Equal(1180.0, history[0].AltitudeMeters);
    }

    private static NormalizedAviationMessage CreateMessage(string id, DateTimeOffset receivedAtUtc)
    {
        return new NormalizedAviationMessage(
            id,
            1,
            receivedAtUtc,
            AviationMessageKind.Acars,
            "ACARS sample",
            new AcarsMessageDetails("2L", "POS", null, "OOOI/position candidate", "2L POS N123AB", "POS N123AB"),
            "N123AB",
            "VHF ACARS",
            136.8,
            ParserConfidence.Candidate,
            new IngestionProvenance(
                IngestionPath.FixtureReplay,
                "fixture-local-acars",
                "Local ACARS fixture replay",
                null,
                "fixture/acars-v1",
                "SqliteRecordStoreTests",
                "0.1.0",
                receivedAtUtc,
                receivedAtUtc,
                "fixtures/acars/local-vhf/test.txt"),
            "2L POS N123AB",
            []);
    }

    private static AircraftTrackSnapshot CreateTrack(DateTimeOffset updatedAtUtc)
    {
        return new AircraftTrackSnapshot(
            "N123AB",
            updatedAtUtc,
            44.75,
            -93.5,
            33000,
            "sample-satcom-json",
            ParserConfidence.Observed,
            "N123AB",
            null,
            null,
            "ADS-C over SATCOM",
            "adsc-N123AB",
            false,
            new IngestionProvenance(
                IngestionPath.ExternalProcess,
                "sample-satcom-json",
                "Sample SATCOM Aero decoded output",
                "satcom-import",
                "application/x-ndjson; domain=satcom-aero",
                "JsonlRecordStoreTests",
                "0.1.0",
                updatedAtUtc,
                updatedAtUtc,
                "fixtures/imports/satcom/test.ndjson:1"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AeroHubTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
