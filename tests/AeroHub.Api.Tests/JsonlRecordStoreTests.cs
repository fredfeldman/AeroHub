using AeroHub.Contracts;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class JsonlRecordStoreTests
{
    [Fact]
    public void SaveMessage_deduplicates_by_id_and_reloads_from_disk()
    {
        using var testDirectory = new TemporaryDirectory();
        var store = new JsonlRecordStore(testDirectory.Path);
        var message = CreateMessage("message-1", DateTimeOffset.UnixEpoch);

        store.SaveMessage(message);
        store.SaveMessage(message);

        var reloaded = new JsonlRecordStore(testDirectory.Path);
        var messages = reloaded.GetRecentMessages(10);

        var saved = Assert.Single(messages);
        Assert.Equal("message-1", saved.Id);
        Assert.Equal(IngestionPath.FixtureReplay, saved.Provenance.Path);
    }

    [Fact]
    public void SaveTrack_keeps_latest_track_and_reload_preserves_provenance()
    {
        using var testDirectory = new TemporaryDirectory();
        var store = new JsonlRecordStore(testDirectory.Path);
        var oldTrack = CreateTrack(DateTimeOffset.UnixEpoch);
        var newTrack = oldTrack with { UpdatedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1), AltitudeFeet = 34000 };

        Assert.True(store.SaveTrack(oldTrack));
        Assert.True(store.SaveTrack(newTrack));
        Assert.False(store.SaveTrack(oldTrack));

        var reloaded = new JsonlRecordStore(testDirectory.Path);
        var track = Assert.Single(reloaded.GetCurrentTracks(DateTimeOffset.UnixEpoch.AddMinutes(2), TimeSpan.FromMinutes(15)));

        Assert.Equal(34000, track.AltitudeFeet);
        Assert.Equal("adsc-N123AB", track.CorrelationGroupId);
    }

    [Fact]
    public void ApplyRetention_removes_expired_records()
    {
        using var testDirectory = new TemporaryDirectory();
        var store = new JsonlRecordStore(testDirectory.Path);
        var now = DateTimeOffset.UnixEpoch.AddDays(10);

        store.SaveMessage(CreateMessage("old", now.AddDays(-5)));
        store.SaveMessage(CreateMessage("new", now));
        store.SaveTrack(CreateTrack(now.AddDays(-5)));
        store.SaveImportDiagnostic(CreateDiagnostic(now.AddDays(-5)));

        var result = store.ApplyRetention(new StorageRetentionPolicy(1, 1, 1), now);

        Assert.Equal(1, result.MessagesRemoved);
        Assert.Equal(1, result.TracksRemoved);
        Assert.Equal(1, result.DiagnosticsRemoved);
        Assert.Single(store.GetRecentMessages(10));
        Assert.Empty(store.GetCurrentTracks(now, TimeSpan.FromMinutes(15)));
        Assert.Empty(store.GetImportDiagnostics(10));
    }

    [Fact]
    public void ExportSnapshot_writes_replayable_jsonl_files()
    {
        using var testDirectory = new TemporaryDirectory();
        var store = new JsonlRecordStore(testDirectory.Path);

        store.SaveMessage(CreateMessage("message-1", DateTimeOffset.UnixEpoch));
        store.SaveTrack(CreateTrack(DateTimeOffset.UnixEpoch));
        store.SaveImportDiagnostic(CreateDiagnostic(DateTimeOffset.UnixEpoch));

        var result = store.ExportSnapshot("test-export", DateTimeOffset.UnixEpoch);
        var exported = new JsonlRecordStore(result.ExportPath);

        Assert.Equal(1, result.MessagesExported);
        Assert.Equal(1, result.TracksExported);
        Assert.Equal(1, result.DiagnosticsExported);
        Assert.Single(exported.GetRecentMessages(10));
        Assert.Single(exported.GetCurrentTracks(DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(15)));
        Assert.Single(exported.GetImportDiagnostics(10));
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
                "JsonlRecordStoreTests",
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

    private static ImportDiagnostic CreateDiagnostic(DateTimeOffset occurredAtUtc)
    {
        return new ImportDiagnostic(
            "diagnostic-1",
            occurredAtUtc,
            "local-acars-ndjson",
            "Warning",
            "IMPORT_SCHEMA_MISMATCH",
            "Missing payload.",
            "fixtures/imports/acars/test.ndjson:1");
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