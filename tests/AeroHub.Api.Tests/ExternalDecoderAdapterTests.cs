using AeroHub.Contracts;
using AeroHub.Core;
using AeroHub.Decoders.Acars;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class ExternalDecoderAdapterTests
{
    [Fact]
    public async Task Dumphfdl_import_routes_acars_and_preserves_ads_c_placeholder()
    {
        var manager = CreateImportManager(out var messageBus);

        var result = await manager.StartImportAsync("sample-dumphfdl-json");
        var messages = messageBus.GetRecent(10);
        var diagnostics = manager.GetDiagnostics(10);

        Assert.Equal(2, result.AcceptedRecords);
        Assert.Equal(1, result.RejectedRecords);
        Assert.Contains(messages, message => message.Kind == AviationMessageKind.Acars && message.Transport == "HFDL" && message.Provenance.SourceApp == "dumphfdl");
        Assert.Contains(messages, message => message.Kind == AviationMessageKind.AdsC && message.Warnings.Any(warning => warning.Code == "EXTERNAL_PAYLOAD_PLACEHOLDER"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_LINK_VALIDATION_FAILED");
    }

    [Fact]
    public async Task Dumpvdl2_import_routes_acars_and_preserves_cpdlc_placeholder()
    {
        var manager = CreateImportManager(out var messageBus);

        var result = await manager.StartImportAsync("sample-dumpvdl2-json");
        var messages = messageBus.GetRecent(10);
        var diagnostics = manager.GetDiagnostics(10);

        Assert.Equal(2, result.AcceptedRecords);
        Assert.Equal(1, result.RejectedRecords);
        Assert.Contains(messages, message => message.Kind == AviationMessageKind.Acars && message.Transport == "VDL2" && message.Provenance.SourceApp == "dumpvdl2");
        Assert.Contains(messages, message => message.Kind == AviationMessageKind.Cpdlc && message.Warnings.Any(warning => warning.Code == "EXTERNAL_PAYLOAD_PLACEHOLDER"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "IMPORT_SCHEMA_MISMATCH");
    }

    [Fact]
    public async Task Satcom_import_preserves_cpdlc_ads_c_and_proprietary_payloads_with_metadata()
    {
        var trackStore = new InMemoryAircraftTrackStore(new TestClock());
        var manager = CreateImportManager(out var messageBus, trackStore);

        var result = await manager.StartImportAsync("sample-satcom-json");
        var messages = messageBus.GetRecent(10);
        var tracks = trackStore.GetCurrentTracks();

        Assert.Equal(3, result.AcceptedRecords);
        Assert.Equal(0, result.RejectedRecords);
        Assert.Contains(messages, message => message.Kind == AviationMessageKind.Cpdlc && message.Datalink?.MessageReference == "UM79" && message.Satcom?.Satellite == "Inmarsat sample");
        Assert.Contains(messages, message => message.Kind == AviationMessageKind.AdsC && message.Datalink?.Latitude == 44.750 && message.Satcom?.ReassemblyState == "complete");
        Assert.Contains(messages, message => message.Kind == AviationMessageKind.Satcom && message.Datalink?.Category == "Unknown/proprietary SATCOM");
        Assert.Contains(tracks, track => track.AircraftIdentifier == "N701SA" && track.SourceType == "ADS-C over SATCOM" && track.CorrelationGroupId == "adsc-N701SA");
    }

    [Theory]
    [InlineData("crash", SourceState.Degraded, "EXTERNAL_DECODER_CRASH")]
    [InlineData("timeout", SourceState.Degraded, "EXTERNAL_DECODER_TIMEOUT")]
    [InlineData("malformed-output", SourceState.Degraded, "EXTERNAL_DECODER_MALFORMED_OUTPUT")]
    [InlineData("restart-limit", SourceState.Failed, "EXTERNAL_DECODER_RESTART_LIMIT")]
    public async Task Process_supervision_surfaces_failure_scenarios(string scenario, SourceState expectedState, string expectedDiagnostic)
    {
        var manager = new ExternalDecoderProcessManager(new TestClock());

        var result = await manager.SimulateAsync("dumphfdl", scenario);
        var process = manager.GetProcesses().Single(process => process.Id == "dumphfdl");

        Assert.Equal(expectedState, result.State);
        Assert.Equal(expectedState, process.State);
        Assert.Contains(manager.GetDiagnostics(10), diagnostic => diagnostic.Code == expectedDiagnostic);
    }

    [Fact]
    public async Task Dump1090_process_is_supervised_and_reports_detected_version_on_start()
    {
        var manager = new ExternalDecoderProcessManager(new TestClock());

        var result = await manager.SimulateAsync("dump1090", "start");
        var process = manager.GetProcesses().Single(process => process.Id == "dump1090");

        Assert.Equal(SourceState.Online, result.State);
        Assert.Equal("dump1090 sample-1", process.DetectedVersion);
        Assert.Contains(manager.GetDiagnostics(10), diagnostic => diagnostic.Code == "EXTERNAL_DECODER_STARTED" && diagnostic.ProcessId == "dump1090");
    }

    private static FileNdjsonImportManager CreateImportManager(out InMemoryMessageBus messageBus, IAircraftTrackStore? trackStore = null)
    {
        var clock = new TestClock();
        messageBus = new InMemoryMessageBus();
        return new FileNdjsonImportManager(messageBus, clock, new AcarsMessageParser(), trackStore ?? new InMemoryAircraftTrackStore(clock), new NullAircraftRegistryLookup());
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
}