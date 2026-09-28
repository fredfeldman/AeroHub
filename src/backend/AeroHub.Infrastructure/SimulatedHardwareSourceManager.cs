using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class SimulatedHardwareSourceManager : IHardwareSourceManager
{
    private const int MaximumDiagnostics = 200;
    private readonly IClock _clock;
    private readonly ISettingsStore? _settingsStore;
    private readonly object _gate = new();
    private readonly List<HardwareSourceDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, HardwareSourceSnapshot> _sources = new(StringComparer.OrdinalIgnoreCase);
    private int _diagnosticSequence;

    public SimulatedHardwareSourceManager(IClock clock, ISettingsStore? settingsStore = null)
    {
        _clock = clock;
        _settingsStore = settingsStore;

        var defaultSources = new List<HardwareSourceSnapshot>
        {
            CreateSource("file-audio-iq", "File/audio IQ input", "FileAudioInputAdapter", "file-audio", new HardwareSourceCapabilities(48000, 192000, 48000, 0.1, 30000, ["software-level"], true, "timestamped-audio-buffers", true)),
            CreateSource("rtl-tcp-local", "rtl_tcp local SDR", "RtlTcpSourceAdapter", "rtl-tcp", new HardwareSourceCapabilities(225001, 2400000, 2400000, 24, 1766, ["manual-tenth-db", "agc"], true, "raw-u8-interleaved-iq", false))
        };

        var savedSources = _settingsStore?.GetSettings().HardwareSources;

        foreach (var def in defaultSources)
        {
            var saved = savedSources?.FirstOrDefault(s => string.Equals(s.SourceId, def.Id, StringComparison.OrdinalIgnoreCase));
            var freq = saved?.FrequencyMHz ?? def.Metrics.TunedFrequencyMHz;
            var rate = saved?.SampleRateHz ?? def.Metrics.ConfiguredSampleRateHz;

            _sources[def.Id] = def with
            {
                Metrics = def.Metrics with
                {
                    TunedFrequencyMHz = freq,
                    ConfiguredSampleRateHz = rate
                }
            };
        }
    }

    public event EventHandler<HardwareSourceSnapshot>? SourceStateChanged;

    public event EventHandler<HardwareSourceDiagnostic>? DiagnosticReceived;

    public IReadOnlyList<HardwareSourceSnapshot> GetSources()
    {
        lock (_gate)
        {
            return _sources.Values.OrderBy(source => source.Id).ToArray();
        }
    }

    public IReadOnlyList<HardwareSourceDiagnostic> GetDiagnostics(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumDiagnostics);

        lock (_gate)
        {
            return _diagnostics
                .OrderByDescending(diagnostic => diagnostic.OccurredAtUtc)
                .Take(boundedLimit)
                .ToArray();
        }
    }

    public Task<HardwareSourceSnapshot> StartAsync(HardwareSourceStartRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = GetSource(request.SourceId);
        ValidateRequest(request, source);

        if (request.RequiresExclusiveOwnership || source.Capabilities.RequiresExclusiveOwnership)
        {
            var owner = GetSources().FirstOrDefault(candidate => candidate.State is SourceState.Starting or SourceState.Online && candidate.OwnerDecoderId is not null);

            if (owner is not null && !string.Equals(owner.Id, request.SourceId, StringComparison.OrdinalIgnoreCase))
            {
                var blocked = source with
                {
                    State = SourceState.Failed,
                    LastError = $"Exclusive source already owned by {owner.OwnerDecoderId} on {owner.Id}."
                };
                SetSource(blocked);
                AddDiagnostic(blocked.Id, "Error", "HARDWARE_SOURCE_OWNERSHIP_BLOCKED", blocked.LastError);
                return Task.FromResult(blocked);
            }
        }

        var updatedMetrics = source.Metrics with
        {
            State = SourceState.Online,
            ConfiguredSampleRateHz = request.SampleRateHz,
            ConfiguredBandwidthHz = request.BandwidthHz,
            TunedFrequencyMHz = request.FrequencyMHz,
            BuffersReceived = source.Metrics.BuffersReceived + 16,
            DroppedBuffers = source.Metrics.DroppedBuffers,
            SignalLevelDbfs = -32.5,
            SnrDb = 18.4,
            QueueDepth = 2,
            IsClipping = false,
            LastBufferAtUtc = _clock.UtcNow
        };
        var updated = source with
        {
            State = SourceState.Online,
            OwnerDecoderId = request.DecoderId,
            Metrics = updatedMetrics,
            LastError = null
        };

        SetSource(updated);
        PersistSourceSettings();
        AddDiagnostic(updated.Id, "Info", "HARDWARE_SOURCE_STARTED", $"{updated.Name} started for decoder {request.DecoderId}.");
        return Task.FromResult(updated);
    }

    public Task<HardwareSourceSnapshot> StopAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = GetSource(sourceId);
        var updatedMetrics = source.Metrics with
        {
            State = SourceState.Offline,
            QueueDepth = 0,
            IsClipping = false
        };
        var updated = source with
        {
            State = SourceState.Offline,
            OwnerDecoderId = null,
            Metrics = updatedMetrics,
            LastError = null
        };

        SetSource(updated);
        AddDiagnostic(updated.Id, "Info", "HARDWARE_SOURCE_STOPPED", $"{updated.Name} stopped.");
        return Task.FromResult(updated);
    }

    public Task<HardwareSourceSimulationResult> SimulateAsync(string sourceId, string scenario, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = GetSource(sourceId);
        var normalizedScenario = scenario.Trim().ToLowerInvariant();
        var updated = normalizedScenario switch
        {
            "buffer-drop" => source with
            {
                State = SourceState.Degraded,
                Metrics = source.Metrics with
                {
                    State = SourceState.Degraded,
                    BuffersReceived = source.Metrics.BuffersReceived + 32,
                    DroppedBuffers = source.Metrics.DroppedBuffers + 7,
                    QueueDepth = 9,
                    LastBufferAtUtc = _clock.UtcNow
                },
                LastError = "Input queue dropped buffers under load."
            },
            "reconnect" => source with
            {
                State = SourceState.Online,
                Metrics = source.Metrics with
                {
                    State = SourceState.Online,
                    ReconnectCount = source.Metrics.ReconnectCount + 1,
                    QueueDepth = 1,
                    LastBufferAtUtc = _clock.UtcNow
                },
                LastError = null
            },
            "clipping" => source with
            {
                State = SourceState.Degraded,
                Metrics = source.Metrics with
                {
                    State = SourceState.Degraded,
                    BuffersReceived = source.Metrics.BuffersReceived + 12,
                    SignalLevelDbfs = -1.2,
                    SnrDb = 7.8,
                    QueueDepth = 4,
                    IsClipping = true,
                    LastBufferAtUtc = _clock.UtcNow
                },
                LastError = "Input level is clipping."
            },
            "failure" => source with
            {
                State = SourceState.Failed,
                Metrics = source.Metrics with
                {
                    State = SourceState.Failed,
                    QueueDepth = 0,
                    IsClipping = false
                },
                LastError = "Adapter stopped after unrecoverable input failure."
            },
            _ => throw new InvalidOperationException($"Unknown hardware source scenario '{scenario}'.")
        };

        SetSource(updated);
        AddDiagnostic(updated.Id, DiagnosticSeverityFor(normalizedScenario), DiagnosticCodeFor(normalizedScenario), updated.LastError ?? $"{updated.Name} recovered after {normalizedScenario}.");
        return Task.FromResult(new HardwareSourceSimulationResult(updated.Id, normalizedScenario, updated.State, updated.Metrics.DroppedBuffers, updated.Metrics.ReconnectCount, updated.Metrics.IsClipping, updated.LastError));
    }

    private static HardwareSourceSnapshot CreateSource(string id, string name, string adapterName, string inputKind, HardwareSourceCapabilities capabilities)
    {
        return new HardwareSourceSnapshot(
            id,
            name,
            SourceState.Offline,
            adapterName,
            inputKind,
            null,
            capabilities,
            new HardwareSourceMetrics(id, SourceState.Offline, capabilities.MinimumSampleRateHz, Math.Min(capabilities.MaximumBandwidthHz, capabilities.MinimumSampleRateHz), null, 0, 0, 0, null, null, 0, false, null),
            null);
    }

    private HardwareSourceSnapshot GetSource(string sourceId)
    {
        lock (_gate)
        {
            if (_sources.TryGetValue(sourceId, out var source))
            {
                return source;
            }
        }

        throw new InvalidOperationException($"Unknown hardware source '{sourceId}'.");
    }

    private static void ValidateRequest(HardwareSourceStartRequest request, HardwareSourceSnapshot source)
    {
        if (string.IsNullOrWhiteSpace(request.DecoderId))
        {
            throw new InvalidOperationException("A decoder owner is required before starting a hardware source.");
        }

        if (request.SampleRateHz < source.Capabilities.MinimumSampleRateHz || request.SampleRateHz > source.Capabilities.MaximumSampleRateHz)
        {
            throw new InvalidOperationException($"Sample rate {request.SampleRateHz} Hz is outside {source.Name} capabilities.");
        }

        if (request.BandwidthHz <= 0 || request.BandwidthHz > source.Capabilities.MaximumBandwidthHz)
        {
            throw new InvalidOperationException($"Bandwidth {request.BandwidthHz} Hz is outside {source.Name} capabilities.");
        }

        if (request.FrequencyMHz is not null && (request.FrequencyMHz < source.Capabilities.MinimumFrequencyMHz || request.FrequencyMHz > source.Capabilities.MaximumFrequencyMHz))
        {
            throw new InvalidOperationException($"Frequency {request.FrequencyMHz} MHz is outside {source.Name} tuning range.");
        }
    }

    private void SetSource(HardwareSourceSnapshot snapshot)
    {
        lock (_gate)
        {
            _sources[snapshot.Id] = snapshot;
        }

        SourceStateChanged?.Invoke(this, snapshot);
    }

    private void PersistSourceSettings()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var current = _settingsStore.GetSettings();
        var sourceSettingsList = _sources.Values.Select(s => new HardwareSourceSettings(
            s.Id,
            s.Metrics.TunedFrequencyMHz,
            s.Metrics.ConfiguredSampleRateHz,
            0.0,
            false
        )).ToList();

        _settingsStore.UpdateSettings(current with { HardwareSources = sourceSettingsList });
    }

    private void AddDiagnostic(string sourceId, string severity, string code, string message)
    {
        var diagnostic = new HardwareSourceDiagnostic(
            $"hardware-source-diagnostic-{Interlocked.Increment(ref _diagnosticSequence)}",
            _clock.UtcNow,
            sourceId,
            severity,
            code,
            message);

        lock (_gate)
        {
            _diagnostics.Add(diagnostic);

            if (_diagnostics.Count > MaximumDiagnostics)
            {
                _diagnostics.RemoveRange(0, _diagnostics.Count - MaximumDiagnostics);
            }
        }

        DiagnosticReceived?.Invoke(this, diagnostic);
    }

    private static string DiagnosticSeverityFor(string scenario)
    {
        return scenario == "failure" ? "Error" : scenario == "reconnect" ? "Info" : "Warning";
    }

    private static string DiagnosticCodeFor(string scenario)
    {
        return scenario switch
        {
            "buffer-drop" => "HARDWARE_SOURCE_BUFFER_DROP",
            "reconnect" => "HARDWARE_SOURCE_RECONNECTED",
            "clipping" => "HARDWARE_SOURCE_CLIPPING",
            "failure" => "HARDWARE_SOURCE_FAILED",
            _ => "HARDWARE_SOURCE_EVENT"
        };
    }
}
