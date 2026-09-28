using AeroHub.Contracts;

namespace AeroHub.Core;

public interface ISyntheticRfStreamService
{
    event EventHandler<SpectrumFrame>? SpectrumFrameProduced;

    event EventHandler<WaterfallRows>? WaterfallRowsProduced;

    event EventHandler<StreamMetricsSnapshot>? MetricsUpdated;

    StreamMetricsSnapshot GetMetrics();

    Task<SyntheticStreamReplayResult> ReplayAsync(int speedMultiplier, CancellationToken cancellationToken = default);
}