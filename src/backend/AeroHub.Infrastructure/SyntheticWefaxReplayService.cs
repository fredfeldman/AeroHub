using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class SyntheticWefaxReplayService : IWefaxReplayService
{
    private const string SourceId = "synthetic-wefax-replay";
    private const int Width = 576;
    private readonly IClock _clock;
    private readonly ISettingsStore? _settingsStore;
    private readonly object _gate = new();
    private long _sequence;
    private bool _isInverted;
    private double _slantCorrection;
    private WefaxDecoderState _state;

    public SyntheticWefaxReplayService(IClock clock, ISettingsStore? settingsStore = null)
    {
        _clock = clock;
        _settingsStore = settingsStore;

        var wefaxSettings = _settingsStore?.GetSettings().Wefax ?? new WefaxDecoderSettings();
        _isInverted = wefaxSettings.IsInverted;
        _slantCorrection = Math.Clamp(wefaxSettings.SlantCorrection, -3.0, 3.0);

        _state = new WefaxDecoderState(
            SourceId,
            SourceState.Offline,
            WefaxSyncState.Searching,
            wefaxSettings.Ioc > 0 ? wefaxSettings.Ioc : 576,
            wefaxSettings.LineRateRpm > 0 ? wefaxSettings.LineRateRpm : 120,
            _isInverted,
            _slantCorrection,
            Width,
            0,
            null,
            []);

        if (_settingsStore is not null)
        {
            _settingsStore.SettingsChanged += OnSettingsChanged;
        }
    }

    public event EventHandler<WefaxLineBatch>? LinesPublished;

    public event EventHandler<WefaxDecoderState>? StateChanged;

    public WefaxDecoderState GetState()
    {
        lock (_gate)
        {
            return _state;
        }
    }

    public Task<WefaxReplayResult> ReplayAsync(int lineCount, bool partial, CancellationToken cancellationToken = default)
    {
        var boundedLineCount = Math.Clamp(lineCount, 1, 180);
        var replayedAtUtc = _clock.UtcNow;

        SetState(SourceState.Starting, WefaxSyncState.Searching, 0, null, []);

        for (var lineNumber = 0; lineNumber < boundedLineCount; lineNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syncState = GetSyncState(lineNumber, boundedLineCount, partial);
            var line = CreateLine(lineNumber, syncState);
            var warnings = partial && lineNumber >= boundedLineCount - 6
                ? new[] { new AviationWarning("WEFAX_PARTIAL_CAPTURE", "Synthetic WEFAX replay ended with sync degraded; partial image preserved.", "Warning") }
                : [];

            SetState(SourceState.Online, syncState, lineNumber + 1, line.GeneratedAtUtc, warnings);
            LinesPublished?.Invoke(this, new WefaxLineBatch(SourceId, line.Sequence, line.GeneratedAtUtc, [line], GetState()));
        }

        var finalState = GetState().SyncState;
        return Task.FromResult(new WefaxReplayResult(SourceId, boundedLineCount, partial, finalState, replayedAtUtc));
    }

    public WefaxDecoderState SetControls(bool isInverted, double slantCorrection)
    {
        lock (_gate)
        {
            _isInverted = isInverted;
            _slantCorrection = Math.Clamp(slantCorrection, -3, 3);
            _state = _state with
            {
                IsInverted = _isInverted,
                SlantCorrection = _slantCorrection
            };
        }

        if (_settingsStore is not null)
        {
            var current = _settingsStore.GetSettings();
            _settingsStore.UpdateSettings(current with
            {
                Wefax = new WefaxDecoderSettings(_isInverted, _slantCorrection, _state.Ioc, _state.LineRateRpm)
            });
        }

        StateChanged?.Invoke(this, GetState());
        return GetState();
    }

    private void OnSettingsChanged(object? sender, DecoderSettings settings)
    {
        lock (_gate)
        {
            var wefax = settings.Wefax;
            _isInverted = wefax.IsInverted;
            _slantCorrection = Math.Clamp(wefax.SlantCorrection, -3.0, 3.0);
            _state = _state with
            {
                IsInverted = _isInverted,
                SlantCorrection = _slantCorrection,
                Ioc = wefax.Ioc > 0 ? wefax.Ioc : _state.Ioc,
                LineRateRpm = wefax.LineRateRpm > 0 ? wefax.LineRateRpm : _state.LineRateRpm
            };
        }

        StateChanged?.Invoke(this, GetState());
    }

    private WefaxImageLine CreateLine(int lineNumber, WefaxSyncState syncState)
    {
        var generatedAtUtc = _clock.UtcNow;
        var sequence = Interlocked.Increment(ref _sequence);
        var pixels = new byte[Width];
        var slantOffset = (int)Math.Round(_slantCorrection * lineNumber);

        for (var x = 0; x < Width; x++)
        {
            var shifted = (x + slantOffset + Width) % Width;
            var chartGrid = shifted % 72 < 2 || lineNumber % 24 < 2 ? 48 : 0;
            var pressureCurve = 100 + 80 * Math.Sin((shifted * 0.024) + (lineNumber * 0.11));
            var coastline = Math.Abs((shifted % 144) - 72) < 3 ? 120 : 0;
            var value = (byte)Math.Clamp(chartGrid + pressureCurve + coastline, 0, 255);
            pixels[x] = _isInverted ? (byte)(255 - value) : value;
        }

        return new WefaxImageLine(
            SourceId,
            sequence,
            generatedAtUtc,
            lineNumber + 1,
            Width,
            syncState,
            syncState == WefaxSyncState.Locked ? 0.96 : syncState == WefaxSyncState.Corrected ? 0.78 : syncState == WefaxSyncState.Coasting ? 0.51 : 0.22,
            new WefaxToneMetrics(0.84, syncState == WefaxSyncState.Lost ? 0.18 : 0.71, generatedAtUtc),
            pixels);
    }

    private static WefaxSyncState GetSyncState(int lineNumber, int lineCount, bool partial)
    {
        if (lineNumber < 3)
        {
            return WefaxSyncState.Searching;
        }

        if (partial && lineNumber > lineCount - 4)
        {
            return WefaxSyncState.Lost;
        }

        if (partial && lineNumber > lineCount - 8)
        {
            return WefaxSyncState.Coasting;
        }

        return lineNumber % 19 == 0 ? WefaxSyncState.Corrected : WefaxSyncState.Locked;
    }

    private void SetState(SourceState state, WefaxSyncState syncState, int linesReceived, DateTimeOffset? lastLineAtUtc, IReadOnlyList<AviationWarning> warnings)
    {
        lock (_gate)
        {
            _state = _state with
            {
                State = state,
                SyncState = syncState,
                IsInverted = _isInverted,
                SlantCorrection = _slantCorrection,
                LinesReceived = linesReceived,
                LastLineAtUtc = lastLineAtUtc,
                Warnings = warnings
            };
        }

        StateChanged?.Invoke(this, GetState());
    }
}