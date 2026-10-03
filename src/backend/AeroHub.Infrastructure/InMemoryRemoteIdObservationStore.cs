using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class InMemoryRemoteIdObservationStore : IRemoteIdObservationStore
{
    private const int MaximumObservations = 1000;
    private readonly object _gate = new();
    private readonly List<RemoteIdObservation> _observations = [];

    public event EventHandler<RemoteIdObservation>? ObservationReceived;

    public IReadOnlyList<RemoteIdObservation> GetRecent(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumObservations);

        lock (_gate)
        {
            return _observations
                .OrderByDescending(observation => observation.ReceivedAtUtc)
                .Take(boundedLimit)
                .ToArray();
        }
    }

    public void Add(RemoteIdObservation observation)
    {
        lock (_gate)
        {
            _observations.Add(observation);

            if (_observations.Count > MaximumObservations)
            {
                _observations.RemoveRange(0, _observations.Count - MaximumObservations);
            }
        }

        ObservationReceived?.Invoke(this, observation);
    }
}