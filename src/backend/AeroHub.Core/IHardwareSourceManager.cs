using AeroHub.Contracts;

namespace AeroHub.Core;

public interface IHardwareSourceManager
{
    event EventHandler<HardwareSourceSnapshot>? SourceStateChanged;

    event EventHandler<HardwareSourceDiagnostic>? DiagnosticReceived;

    IReadOnlyList<HardwareSourceSnapshot> GetSources();

    IReadOnlyList<HardwareSourceDiagnostic> GetDiagnostics(int limit);

    Task<HardwareSourceSnapshot> StartAsync(HardwareSourceStartRequest request, CancellationToken cancellationToken = default);

    Task<HardwareSourceSnapshot> StopAsync(string sourceId, CancellationToken cancellationToken = default);

    Task<HardwareSourceSimulationResult> SimulateAsync(string sourceId, string scenario, CancellationToken cancellationToken = default);
}
