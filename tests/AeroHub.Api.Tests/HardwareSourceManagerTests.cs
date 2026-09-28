using AeroHub.Contracts;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class HardwareSourceManagerTests
{
    [Fact]
    public void GetSources_exposes_file_audio_and_rtl_tcp_capabilities()
    {
        var manager = new SimulatedHardwareSourceManager(new TestClock());

        var sources = manager.GetSources();

        var fileAudio = Assert.Single(sources, source => source.Id == "file-audio-iq");
        Assert.Equal("FileAudioInputAdapter", fileAudio.AdapterName);
        Assert.Equal("timestamped-audio-buffers", fileAudio.Capabilities.StreamFraming);
        Assert.True(fileAudio.Capabilities.AcknowledgesTuneCommands);

        var rtlTcp = Assert.Single(sources, source => source.Id == "rtl-tcp-local");
        Assert.Equal("RtlTcpSourceAdapter", rtlTcp.AdapterName);
        Assert.Equal("raw-u8-interleaved-iq", rtlTcp.Capabilities.StreamFraming);
        Assert.False(rtlTcp.Capabilities.AcknowledgesTuneCommands);
        Assert.Contains("manual-tenth-db", rtlTcp.Capabilities.GainControls);
    }

    [Fact]
    public async Task StartAsync_enforces_exclusive_source_ownership()
    {
        var manager = new SimulatedHardwareSourceManager(new TestClock());

        var started = await manager.StartAsync(new HardwareSourceStartRequest("file-audio-iq", "acars-message-core", 48000, 24000, 136.8, true));
        var blocked = await manager.StartAsync(new HardwareSourceStartRequest("rtl-tcp-local", "wefax-core", 1024000, 200000, 137.1, true));

        Assert.Equal(SourceState.Online, started.State);
        Assert.Equal("acars-message-core", started.OwnerDecoderId);
        Assert.Equal(SourceState.Failed, blocked.State);
        Assert.Contains("Exclusive source already owned", blocked.LastError);
        Assert.Contains(manager.GetDiagnostics(10), diagnostic => diagnostic.Code == "HARDWARE_SOURCE_OWNERSHIP_BLOCKED");
    }

    [Theory]
    [InlineData("buffer-drop", SourceState.Degraded, "HARDWARE_SOURCE_BUFFER_DROP")]
    [InlineData("reconnect", SourceState.Online, "HARDWARE_SOURCE_RECONNECTED")]
    [InlineData("clipping", SourceState.Degraded, "HARDWARE_SOURCE_CLIPPING")]
    [InlineData("failure", SourceState.Failed, "HARDWARE_SOURCE_FAILED")]
    public async Task SimulateAsync_updates_metrics_and_diagnostics(string scenario, SourceState expectedState, string expectedDiagnostic)
    {
        var manager = new SimulatedHardwareSourceManager(new TestClock());

        var result = await manager.SimulateAsync("file-audio-iq", scenario);
        var source = manager.GetSources().Single(source => source.Id == "file-audio-iq");

        Assert.Equal(expectedState, result.State);
        Assert.Equal(expectedState, source.State);
        Assert.Equal(expectedState, source.Metrics.State);
        Assert.Contains(manager.GetDiagnostics(10), diagnostic => diagnostic.Code == expectedDiagnostic);
    }

    [Fact]
    public async Task StartAsync_rejects_unsupported_tuning_requests()
    {
        var manager = new SimulatedHardwareSourceManager(new TestClock());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.StartAsync(new HardwareSourceStartRequest("rtl-tcp-local", "acars-message-core", 48000, 24000, 136.8, true)));

        Assert.Contains("Sample rate", exception.Message);
    }

    private sealed class TestClock : AeroHub.Core.IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }
}
