using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

/// <summary>
/// Free, key-less aircraft photo lookup backed by the public planespotters.net API
/// (https://www.planespotters.net/photo/api), attributed via <see cref="AircraftPhotoInfo"/>.
/// Results (including "no photo found") are cached and requests are throttled to stay
/// within planespotters' documented courtesy rate limit of roughly one request per second.
/// </summary>
public sealed class PlanespottersPhotoLookup(IHttpClientFactory httpClientFactory) : IAircraftPhotoLookup
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(1100);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTimeOffset _lastRequestAtUtc = DateTimeOffset.MinValue;

    public async Task<AircraftPhotoInfo?> GetPhotoAsync(string hexAddress, CancellationToken cancellationToken = default)
    {
        var hex = hexAddress.ToLowerInvariant();

        if (_cache.TryGetValue(hex, out var cached) && DateTimeOffset.UtcNow - cached.CachedAtUtc < CacheLifetime)
        {
            return cached.Photo;
        }

        await _throttle.WaitAsync(cancellationToken);

        try
        {
            var waitFor = MinimumRequestInterval - (DateTimeOffset.UtcNow - _lastRequestAtUtc);

            if (waitFor > TimeSpan.Zero)
            {
                await Task.Delay(waitFor, cancellationToken);
            }

            var photo = await FetchAsync(hex, cancellationToken);
            _lastRequestAtUtc = DateTimeOffset.UtcNow;
            _cache[hex] = new CacheEntry(photo, DateTimeOffset.UtcNow);
            return photo;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private async Task<AircraftPhotoInfo?> FetchAsync(string hex, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("planespotters");
            var response = await client.GetFromJsonAsync<PlanespottersResponse>(
                $"https://api.planespotters.net/pub/photos/hex/{hex}",
                cancellationToken);

            var photo = response?.Photos?.FirstOrDefault();

            if (photo?.ThumbnailLarge?.Src is null && photo?.Thumbnail?.Src is null)
            {
                return null;
            }

            return new AircraftPhotoInfo(
                photo.Thumbnail?.Src ?? photo.ThumbnailLarge!.Src!,
                photo.ThumbnailLarge?.Src,
                photo.Photographer,
                photo.Link);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private readonly record struct CacheEntry(AircraftPhotoInfo? Photo, DateTimeOffset CachedAtUtc);

    private sealed record PlanespottersResponse([property: JsonPropertyName("photos")] IReadOnlyList<PlanespottersPhoto>? Photos);

    private sealed record PlanespottersPhoto(
        [property: JsonPropertyName("thumbnail")] PlanespottersImage? Thumbnail,
        [property: JsonPropertyName("thumbnail_large")] PlanespottersImage? ThumbnailLarge,
        [property: JsonPropertyName("link")] string? Link,
        [property: JsonPropertyName("photographer")] string? Photographer);

    private sealed record PlanespottersImage([property: JsonPropertyName("src")] string? Src);
}
