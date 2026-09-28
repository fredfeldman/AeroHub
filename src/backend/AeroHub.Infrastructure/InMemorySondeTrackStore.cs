using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class InMemorySondeTrackStore(IClock clock, IRecordStore? recordStore = null) : ISondeTrackStore
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly Dictionary<string, SondeTelemetrySnapshot> _tracks = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<SondeTelemetrySnapshot>? TrackUpdated;

    public IReadOnlyList<SondeTelemetrySnapshot> GetCurrentTracks()
    {
        lock (_gate)
        {
            IEnumerable<SondeTelemetrySnapshot> sourceTracks = _tracks.Count == 0 && recordStore is not null
                ? recordStore.GetCurrentSondeTracks(clock.UtcNow, StaleAfter)
                : _tracks.Values;

            return sourceTracks
                .Select(ApplyStaleState)
                .Where(track => !track.IsStale)
                .OrderBy(track => track.Serial)
                .ToArray();
        }
    }

    public bool Upsert(SondeTelemetrySnapshot track)
    {
        SondeTelemetrySnapshot normalized;

        lock (_gate)
        {
            if (_tracks.TryGetValue(track.Serial, out var existing) && existing.UpdatedAtUtc >= track.UpdatedAtUtc)
            {
                return false;
            }

            normalized = ApplyStaleState(track);
            _tracks[track.Serial] = normalized;
            recordStore?.SaveSondeTrack(normalized);

            if (normalized.Latitude is { } latitude && normalized.Longitude is { } longitude)
            {
                recordStore?.SaveSondePosition(new SondePositionSample(
                    normalized.Serial,
                    normalized.UpdatedAtUtc,
                    latitude,
                    longitude,
                    normalized.AltitudeMeters,
                    normalized.AscentRateMetersPerSecond));
            }
        }

        TrackUpdated?.Invoke(this, normalized);
        return true;
    }

    private SondeTelemetrySnapshot ApplyStaleState(SondeTelemetrySnapshot track)
    {
        return track with
        {
            IsStale = clock.UtcNow - track.UpdatedAtUtc > StaleAfter
        };
    }
}
