using AeroHub.Contracts;
using AeroHub.Core;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class SyntheticWefaxReplayServiceTests
{
    [Fact]
    public async Task ReplayAsync_publishes_deterministic_lines_and_final_state()
    {
        var service = new SyntheticWefaxReplayService(new TestClock());
        var lines = new List<WefaxLineBatch>();

        service.LinesPublished += (_, batch) => lines.Add(batch);

        var result = await service.ReplayAsync(12, partial: false);
        var state = service.GetState();

        Assert.Equal(12, result.LinesPublished);
        Assert.Equal(12, lines.Count);
        Assert.Equal(576, state.Ioc);
        Assert.Equal(120, state.LineRateRpm);
        Assert.Equal(576, lines[0].Lines[0].Width);
        Assert.Equal(WefaxSyncState.Locked, result.FinalSyncState);
    }

    [Fact]
    public async Task ReplayAsync_preserves_partial_lost_sync_state()
    {
        var service = new SyntheticWefaxReplayService(new TestClock());

        var result = await service.ReplayAsync(12, partial: true);
        var state = service.GetState();

        Assert.True(result.IsPartial);
        Assert.Equal(WefaxSyncState.Lost, state.SyncState);
        Assert.Contains(state.Warnings, warning => warning.Code == "WEFAX_PARTIAL_CAPTURE");
    }

    [Fact]
    public void SetControls_updates_polarity_and_slant()
    {
        var service = new SyntheticWefaxReplayService(new TestClock());

        var state = service.SetControls(isInverted: true, slantCorrection: 2.5);

        Assert.True(state.IsInverted);
        Assert.Equal(2.5, state.SlantCorrection);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }
}