using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class SyntheticRfStreamService(IClock clock) : ISyntheticRfStreamService
{
    private const string SourceId = "synthetic-rf-replay";
    private const int BinCount = 96;
    private readonly object _gate = new();
    private long _sequence;
    private long _spectrumFramesProduced;
    private long _waterfallRowsProduced;
    private int _speedMultiplier;
    private DateTimeOffset? _lastGeneratedAtUtc;
    private SourceState _state = SourceState.Offline;

    public event EventHandler<SpectrumFrame>? SpectrumFrameProduced;

    public event EventHandler<WaterfallRows>? WaterfallRowsProduced;

    public event EventHandler<StreamMetricsSnapshot>? MetricsUpdated;

    public StreamMetricsSnapshot GetMetrics()
    {
        lock (_gate)
        {
            return CreateMetricsSnapshot();
        }
    }

    public Task<SyntheticStreamReplayResult> ReplayAsync(int speedMultiplier, CancellationToken cancellationToken = default)
    {
        var boundedSpeed = Math.Clamp(speedMultiplier, 1, 20);
        var frameCount = boundedSpeed * 5;
        var replayedAtUtc = clock.UtcNow;

        SetState(SourceState.Starting, boundedSpeed);

        for (var index = 0; index < frameCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProduceFrame(index, boundedSpeed);
        }

        SetState(SourceState.Online, boundedSpeed);

        return Task.FromResult(new SyntheticStreamReplayResult(SourceId, boundedSpeed, frameCount, frameCount, replayedAtUtc));
    }

    private void ProduceFrame(int index, int speedMultiplier)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var generatedAtUtc = clock.UtcNow;
        var spectrum = GenerateSpectrumBins(index, speedMultiplier);
        var row = GenerateWaterfallRow(index, speedMultiplier);

        lock (_gate)
        {
            _spectrumFramesProduced++;
            _waterfallRowsProduced++;
            _lastGeneratedAtUtc = generatedAtUtc;
        }

        SpectrumFrameProduced?.Invoke(this, new SpectrumFrame(
            SourceId,
            sequence,
            generatedAtUtc,
            136.800,
            25,
            spectrum));

        WaterfallRowsProduced?.Invoke(this, new WaterfallRows(
            SourceId,
            sequence,
            generatedAtUtc,
            BinCount,
            [new WaterfallRow(sequence, generatedAtUtc, row)]));

        MetricsUpdated?.Invoke(this, GetMetrics());
    }

    private static float[] GenerateSpectrumBins(int frameIndex, int speedMultiplier)
    {
        var bins = new float[BinCount];
        var center = BinCount / 2.0;
        var carrier = 18 + frameIndex % 54;

        for (var index = 0; index < bins.Length; index++)
        {
            var floor = -92 + 7 * Math.Sin((frameIndex + index) * 0.13);
            var broadSignal = 24 * Math.Exp(-Math.Pow((index - center) / 18, 2));
            var carrierSignal = 34 * Math.Exp(-Math.Pow((index - carrier) / 3.2, 2));
            var speedRipple = speedMultiplier * Math.Sin(index * 0.21);
            bins[index] = (float)Math.Clamp(floor + broadSignal + carrierSignal + speedRipple, -110, -25);
        }

        return bins;
    }

    private static byte[] GenerateWaterfallRow(int frameIndex, int speedMultiplier)
    {
        var row = new byte[BinCount];
        var carrier = 18 + frameIndex % 54;

        for (var index = 0; index < row.Length; index++)
        {
            var carrierEnergy = 210 * Math.Exp(-Math.Pow((index - carrier) / 3.8, 2));
            var texture = 28 + 24 * Math.Sin((frameIndex * speedMultiplier + index) * 0.17);
            row[index] = (byte)Math.Clamp(texture + carrierEnergy, 0, 255);
        }

        return row;
    }

    private void SetState(SourceState state, int speedMultiplier)
    {
        lock (_gate)
        {
            _state = state;
            _speedMultiplier = speedMultiplier;
        }

        MetricsUpdated?.Invoke(this, GetMetrics());
    }

    private StreamMetricsSnapshot CreateMetricsSnapshot()
    {
        return new StreamMetricsSnapshot(
            SourceId,
            _state,
            _speedMultiplier,
            _spectrumFramesProduced,
            _waterfallRowsProduced,
            0,
            0,
            0,
            _lastGeneratedAtUtc);
    }
}