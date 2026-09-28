using AeroHub.Contracts;
using AeroHub.Core;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class SyntheticRfStreamServiceTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(5, 25)]
    [InlineData(20, 100)]
    public async Task ReplayAsync_produces_expected_frames_for_speed_multiplier(int speedMultiplier, int expectedFrames)
    {
        var service = new SyntheticRfStreamService(new TestClock());
        var spectrumFrames = new List<SpectrumFrame>();
        var waterfallRows = new List<WaterfallRows>();
        var metrics = new List<StreamMetricsSnapshot>();

        service.SpectrumFrameProduced += (_, frame) => spectrumFrames.Add(frame);
        service.WaterfallRowsProduced += (_, rows) => waterfallRows.Add(rows);
        service.MetricsUpdated += (_, snapshot) => metrics.Add(snapshot);

        var result = await service.ReplayAsync(speedMultiplier);
        var finalMetrics = service.GetMetrics();

        Assert.Equal(expectedFrames, result.SpectrumFramesProduced);
        Assert.Equal(expectedFrames, result.WaterfallRowsProduced);
        Assert.Equal(expectedFrames, spectrumFrames.Count);
        Assert.Equal(expectedFrames, waterfallRows.Count);
        Assert.Equal(expectedFrames, finalMetrics.SpectrumFramesProduced);
        Assert.Equal(expectedFrames, finalMetrics.WaterfallRowsProduced);
        Assert.Equal(SourceState.Online, finalMetrics.State);
        Assert.All(spectrumFrames, frame => Assert.Equal(96, frame.Bins.Count));
        Assert.All(waterfallRows, rows => Assert.Equal(96, rows.Width));
        Assert.NotEmpty(metrics);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }
}