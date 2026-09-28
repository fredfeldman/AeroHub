using AeroHub.Contracts;

namespace AeroHub.Core;

public interface IFeederManager
{
    event EventHandler<FeederDiagnostic>? DiagnosticReceived;

    event EventHandler<FeederStatusSnapshot>? FeederStateChanged;

    IReadOnlyList<FeederStatusSnapshot> GetFeeders();

    IReadOnlyList<FeederDiagnostic> GetDiagnostics(int limit);

    Task<FeederStatusSnapshot> StartFeederAsync(FeederStartRequest request, CancellationToken cancellationToken = default);

    Task<FeederStatusSnapshot> StopFeederAsync(string feederId, CancellationToken cancellationToken = default);

    Task<FeederStatusSnapshot> SendTestFrameAsync(string feederId, CancellationToken cancellationToken = default);
}
