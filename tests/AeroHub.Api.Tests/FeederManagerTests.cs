using AeroHub.Contracts;
using AeroHub.Core;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class FeederManagerTests
{
    [Fact]
    public async Task FlightAwareFeederManager_starts_stops_and_sends_test_frames()
    {
        var clock = new TestClock();
        var manager = new FlightAwareFeederManager(clock);

        var feeders = manager.GetFeeders();
        Assert.Contains(feeders, f => f.Id == "flightaware-beast-outbound" && f.TargetService == "FlightAware");

        var startResult = await manager.StartFeederAsync(new FeederStartRequest("flightaware-beast-outbound", "feed.flightaware.com", 30005, "KDFW-FA-1"));
        Assert.Equal(SourceState.Online, startResult.State);
        Assert.Equal("KDFW-FA-1", startResult.StationId);

        var testResult = await manager.SendTestFrameAsync("flightaware-beast-outbound");
        Assert.Equal(1, testResult.FramesSent);
        Assert.True(testResult.BytesSent > 0);
        Assert.Equal(clock.UtcNow, testResult.LastSentAtUtc);

        var diagnostics = manager.GetDiagnostics(10);
        Assert.Contains(diagnostics, d => d.Code == "FEEDER_STARTED" && d.FeederId == "flightaware-beast-outbound");
        Assert.Contains(diagnostics, d => d.Code == "FEEDER_TEST_FRAME_SENT" && d.FeederId == "flightaware-beast-outbound");

        var stopResult = await manager.StopFeederAsync("flightaware-beast-outbound");
        Assert.Equal(SourceState.Offline, stopResult.State);
    }

    [Fact]
    public void EncodeBeastModeSLongFrame_escapes_0x1A_bytes()
    {
        var utcNow = DateTimeOffset.UnixEpoch;
        byte[] modeSBytes = [0x8D, 0x1A, 0xB2, 0xC3, 0x20, 0x2C, 0xC3, 0x71, 0xC3, 0x2C, 0xE0, 0x57, 0x60, 0x98];

        var encoded = FlightAwareFeederManager.EncodeBeastModeSLongFrame("A1B2C3", modeSBytes, utcNow);

        Assert.Equal(0x1A, encoded[0]);
        Assert.Equal(0x33, encoded[1]); // Mode S long frame
        Assert.Contains(encoded, b => b == 0x1A);

        // Verify byte 0x1A in payload is followed by another 0x1A
        var hexIndex = Array.IndexOf(encoded, (byte)0x8D);
        Assert.True(hexIndex > 0);
        Assert.Equal(0x1A, encoded[hexIndex + 1]);
        Assert.Equal(0x1A, encoded[hexIndex + 2]); // Doubled escape
    }

    [Fact]
    public void FormatBaseStationPositionLine_generates_valid_sbs1_csv()
    {
        var utcNow = DateTimeOffset.UnixEpoch;
        var csv = FlightAwareFeederManager.FormatBaseStationPositionLine("A1B2C3", "AAL123", 33.1041, -96.7083, 34000, 432.5, 88.2, utcNow);

        Assert.StartsWith("MSG,3,1,1,A1B2C3,1,", csv);
        Assert.Contains("AAL123,34000,432.5,88.2,33.10410,-96.70830", csv);
        Assert.EndsWith("\r\n", csv);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }
}
