using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class ExternalDecoderProcessManager(IClock clock) : IExternalDecoderProcessManager
{
    private const int MaximumDiagnostics = 200;
    private readonly object _gate = new();
    private readonly List<ExternalDecoderDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, ExternalDecoderProcessSnapshot> _processes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dumphfdl"] = new ExternalDecoderProcessSnapshot("dumphfdl", "dumphfdl HFDL decoder", SourceState.Offline, "dumphfdl", null, 0, 3, null, null, null),
        ["dumpvdl2"] = new ExternalDecoderProcessSnapshot("dumpvdl2", "dumpvdl2 VDL2 decoder", SourceState.Offline, "dumpvdl2", null, 0, 3, null, null, null),
        ["dump1090"] = new ExternalDecoderProcessSnapshot("dump1090", "dump1090 ADS-B decoder", SourceState.Offline, "dump1090", null, 0, 3, null, null, null)
    };
    private int _diagnosticSequence;

    public event EventHandler<ExternalDecoderDiagnostic>? DiagnosticReceived;

    public event EventHandler<ExternalDecoderProcessSnapshot>? ProcessStateChanged;

    public IReadOnlyList<ExternalDecoderProcessSnapshot> GetProcesses()
    {
        lock (_gate)
        {
            return _processes.Values.OrderBy(process => process.Id).ToArray();
        }
    }

    public IReadOnlyList<ExternalDecoderDiagnostic> GetDiagnostics(int limit)
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

    public Task<ExternalDecoderSimulationResult> SimulateAsync(string processId, string scenario, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_processes.ContainsKey(processId))
        {
            throw new InvalidOperationException($"Unknown external decoder process '{processId}'.");
        }

        var normalizedScenario = scenario.Trim().ToLowerInvariant();
        var current = GetProcess(processId);
        var updated = normalizedScenario switch
        {
            "start" => current with
            {
                State = SourceState.Online,
                DetectedVersion = current.Id switch
                {
                    "dumphfdl" => "dumphfdl sample-1",
                    "dumpvdl2" => "dumpvdl2 sample-1",
                    "dump1090" => "dump1090 sample-1",
                    _ => $"{current.Id} sample-1"
                },
                LastStartedAtUtc = clock.UtcNow,
                LastError = null
            },
            "crash" => current with
            {
                State = SourceState.Degraded,
                RestartCount = Math.Min(current.RestartLimit, current.RestartCount + 1),
                LastError = "Process exited unexpectedly."
            },
            "timeout" => current with
            {
                State = SourceState.Degraded,
                LastError = "No decoder output arrived before timeout."
            },
            "malformed-output" => current with
            {
                State = SourceState.Degraded,
                LastError = "Decoder output failed the adapter contract."
            },
            "restart-limit" => current with
            {
                State = SourceState.Failed,
                RestartCount = current.RestartLimit,
                LastStoppedAtUtc = clock.UtcNow,
                LastError = "Restart limit reached."
            },
            _ => throw new InvalidOperationException($"Unknown external decoder scenario '{scenario}'.")
        };

        SetProcess(updated);
        AddDiagnostic(processId, normalizedScenario);

        return Task.FromResult(new ExternalDecoderSimulationResult(processId, normalizedScenario, updated.State, updated.RestartCount, updated.LastError));
    }

    private ExternalDecoderProcessSnapshot GetProcess(string processId)
    {
        lock (_gate)
        {
            return _processes[processId];
        }
    }

    private void SetProcess(ExternalDecoderProcessSnapshot snapshot)
    {
        lock (_gate)
        {
            _processes[snapshot.Id] = snapshot;
        }

        ProcessStateChanged?.Invoke(this, snapshot);
    }

    private void AddDiagnostic(string processId, string scenario)
    {
        var (severity, code, message) = scenario switch
        {
            "start" => ("Info", "EXTERNAL_DECODER_STARTED", $"External decoder {processId} startup validation succeeded."),
            "crash" => ("Warning", "EXTERNAL_DECODER_CRASH", $"External decoder {processId} exited unexpectedly and is eligible for restart."),
            "timeout" => ("Warning", "EXTERNAL_DECODER_TIMEOUT", $"External decoder {processId} produced no output before timeout."),
            "malformed-output" => ("Warning", "EXTERNAL_DECODER_MALFORMED_OUTPUT", $"External decoder {processId} emitted malformed output."),
            "restart-limit" => ("Error", "EXTERNAL_DECODER_RESTART_LIMIT", $"External decoder {processId} reached its restart limit."),
            _ => ("Info", "EXTERNAL_DECODER_EVENT", $"External decoder {processId} event: {scenario}.")
        };

        var diagnostic = new ExternalDecoderDiagnostic(
            $"external-decoder-diagnostic-{Interlocked.Increment(ref _diagnosticSequence)}",
            clock.UtcNow,
            processId,
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
}