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
            if (_tracks.TryGetValue(track.AircraftIdentifier, out var existing) && existing.UpdatedAtUtc >= track.UpdatedAtUtc)
            {
                return false;
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

    private AircraftTrackSnapshot ApplyStaleState(AircraftTrackSnapshot track)
    {
        return track with
        {
            IsStale = clock.UtcNow - track.UpdatedAtUtc > StaleAfter
        };
    }
}