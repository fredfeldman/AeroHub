using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class InMemoryAircraftTrackStore(IClock clock, IRecordStore? recordStore = null) : IAircraftTrackStore
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly Dictionary<string, AircraftTrackSnapshot> _tracks = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<AircraftTrackSnapshot>? TrackUpdated;

    public IReadOnlyList<AircraftTrackSnapshot> GetCurrentTracks()
    {
        lock (_gate)
        {
            IEnumerable<AircraftTrackSnapshot> sourceTracks = _tracks.Count == 0 && recordStore is not null
                ? recordStore.GetCurrentTracks(clock.UtcNow, StaleAfter)
                : _tracks.Values;

            return sourceTracks
                .Select(ApplyStaleState)
                .Where(track => !track.IsStale)
                .OrderBy(track => track.AircraftIdentifier)
                .ToArray();
        }
    }

    public bool Upsert(AircraftTrackSnapshot track)
    {
        AircraftTrackSnapshot normalized;

        lock (_gate)
        {
            if (_tracks.TryGetValue(track.AircraftIdentifier, out var existing))
            {
                if (existing.UpdatedAtUtc >= track.UpdatedAtUtc)
                {
                    return false;
                }

                track = MergeRemoteIdMetadata(existing, track);
            }

            normalized = ApplyStaleState(track);
            _tracks[track.AircraftIdentifier] = normalized;
            recordStore?.SaveTrack(normalized);

            if (normalized.Latitude is { } latitude && normalized.Longitude is { } longitude)
            {
                recordStore?.SavePosition(new AircraftPositionSample(
                    normalized.AircraftIdentifier,
                    normalized.UpdatedAtUtc,
                    latitude,
                    longitude,
                    normalized.AltitudeFeet,
                    normalized.GroundSpeedKnots,
                    normalized.TrackDegrees));
            }
        }

        TrackUpdated?.Invoke(this, normalized);
        return true;
    }

    private static AircraftTrackSnapshot MergeRemoteIdMetadata(AircraftTrackSnapshot existing, AircraftTrackSnapshot incoming)
    {
        if (!string.Equals(existing.SourceType, "Remote ID drone", StringComparison.Ordinal)
            || !string.Equals(incoming.SourceType, "Remote ID drone", StringComparison.Ordinal))
        {
            return incoming;
        }

        return incoming with
        {
            RemoteIdSerialNumber = incoming.RemoteIdSerialNumber ?? existing.RemoteIdSerialNumber,
            RemoteIdOperatorId = incoming.RemoteIdOperatorId ?? existing.RemoteIdOperatorId,
            RemoteIdOperationType = incoming.RemoteIdOperationType ?? existing.RemoteIdOperationType,
            RemoteIdUasIdType = incoming.RemoteIdUasIdType ?? existing.RemoteIdUasIdType,
            RemoteIdUaType = incoming.RemoteIdUaType ?? existing.RemoteIdUaType,
            RemoteIdOperatorIdType = incoming.RemoteIdOperatorIdType ?? existing.RemoteIdOperatorIdType,
            RemoteIdOperationTypeSource = incoming.RemoteIdOperationTypeSource ?? existing.RemoteIdOperationTypeSource
        };
    }

    private AircraftTrackSnapshot ApplyStaleState(AircraftTrackSnapshot track)
    {
        return track with
        {
            IsStale = clock.UtcNow - track.UpdatedAtUtc > StaleAfter
        };
    }
}