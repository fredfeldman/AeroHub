using AeroHub.Contracts;

namespace AeroHub.Api.Tests;

public class UnitTest1
{
    [Fact]
    public void HealthStatus_records_service_identity()
    {
        var status = new HealthStatus(
            "AeroHub.Api",
            "1.0.0",
            "Test",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            ["health-api"]);

        Assert.Equal("AeroHub.Api", status.ServiceName);
        Assert.Contains("health-api", status.Capabilities);
    }

    [Fact]
    public void NormalizedMessage_preserves_fixture_provenance()
    {
        var receivedAtUtc = DateTimeOffset.UnixEpoch;
        var message = new NormalizedAviationMessage(
            "message-1",
            1,
            receivedAtUtc,
            AviationMessageKind.Acars,
            "ACARS sample",
            new AcarsMessageDetails("2L", "POS", null, "OOOI/position candidate", "2L POS N123AB OUT 1412Z OFF 1427Z", "POS N123AB OUT 1412Z OFF 1427Z"),
            "N123AB",
            "VHF ACARS",
            136.800,
            ParserConfidence.Candidate,
            new IngestionProvenance(
                IngestionPath.FixtureReplay,
                "fixture-local-acars",
                "Local ACARS fixture replay",
                null,
                "fixture/acars-v1",
                "FixtureReplayService",
                "0.1.0",
                receivedAtUtc,
                receivedAtUtc,
                "fixtures/acars/local-vhf/1.txt"),
            "2L POS N123AB OUT 1412Z OFF 1427Z",
            []);

        Assert.Equal(IngestionPath.FixtureReplay, message.Provenance.Path);
        Assert.Equal(136.800, message.FrequencyMHz);
        Assert.Empty(message.Warnings);
    }

    [Fact]
    public void OperatorDiagnosticsSnapshot_includes_runtime_health_and_recent_warnings()
    {
        var checkedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(2);
        var snapshot = new OperatorDiagnosticsSnapshot(
            "AeroHub.Api",
            "1.2.3",
            "Development",
            DateTimeOffset.UnixEpoch,
            checkedAtUtc,
            3,
            2,
            120,
            4,
            "Healthy",
            4096,
            ["IMPORT_SCHEMA_MISMATCH", "HARDWARE_SOURCE_OWNERSHIP_BLOCKED"]);

        Assert.Equal("AeroHub.Api", snapshot.ServiceName);
        Assert.Equal(3, snapshot.ActiveDecoders);
        Assert.Equal(2, snapshot.ActiveImports);
        Assert.Equal(120, snapshot.StreamQueueDepth);
        Assert.Contains("IMPORT_SCHEMA_MISMATCH", snapshot.RecentWarnings);
    }
}