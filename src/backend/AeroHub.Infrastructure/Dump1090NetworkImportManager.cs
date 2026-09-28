using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class Dump1090NetworkImportManager : IDump1090NetworkImportManager, IDisposable
{
    private const string ImportId = "dump1090-network";
    private const int MaximumDiagnostics = 200;
    private const string AdapterName = "Dump1090NetworkImportManager";
    private const string AdapterVersion = "0.1.0";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClock _clock;
    private readonly IAircraftTrackStore _aircraftTrackStore;
    private readonly IAircraftRegistryLookup _aircraftRegistryLookup;
    private readonly ISettingsStore? _settingsStore;

    private readonly object _gate = new();
    private readonly List<ImportDiagnostic> _diagnostics = [];
    private Dump1090ConnectionSnapshot _snapshot;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private int _diagnosticSequence;

    public Dump1090NetworkImportManager(
        IHttpClientFactory httpClientFactory,
        IClock clock,
        IAircraftTrackStore aircraftTrackStore,
        IAircraftRegistryLookup aircraftRegistryLookup,
        ISettingsStore? settingsStore = null)
    {
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _aircraftTrackStore = aircraftTrackStore;
        _aircraftRegistryLookup = aircraftRegistryLookup;
        _settingsStore = settingsStore;

        var dump1090Settings = _settingsStore?.GetSettings().Dump1090;

        _snapshot = new Dump1090ConnectionSnapshot(
            ImportId,
            SourceState.Offline,
            dump1090Settings?.Host,
            dump1090Settings?.Port ?? 8080,
            dump1090Settings?.JsonPath ?? "/data/aircraft.json",
            dump1090Settings?.PollIntervalSeconds ?? 5,
            0,
            0,
            null,
            null);

        if (_settingsStore is not null && dump1090Settings is { AutoConnect: true } && !string.IsNullOrWhiteSpace(dump1090Settings.Host))
        {
            _ = ConnectAsync(new Dump1090ConnectionRequest(
                dump1090Settings.Host,
                dump1090Settings.Port,
                dump1090Settings.JsonPath,
                dump1090Settings.PollIntervalSeconds));
        }
    }

    public event EventHandler<ImportDiagnostic>? DiagnosticReceived;

    public event EventHandler<Dump1090ConnectionSnapshot>? ConnectionStateChanged;

    public Dump1090ConnectionSnapshot GetStatus()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public IReadOnlyList<ImportDiagnostic> GetDiagnostics(int limit)
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

    public Task<Dump1090ConnectionSnapshot> ConnectAsync(Dump1090ConnectionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Host))
        {
            throw new InvalidOperationException("A dump1090 host is required.");
        }

        if (request.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Port must be between 1 and 65535.");
        }

        var pollIntervalSeconds = Math.Clamp(request.PollIntervalSeconds, 1, 3600);
        var jsonPath = string.IsNullOrWhiteSpace(request.JsonPath) ? "/data/aircraft.json" : request.JsonPath;

        StopLoop();

        SetSnapshot(_snapshot with
        {
            State = SourceState.Starting,
            Host = request.Host,
            Port = request.Port,
            JsonPath = jsonPath,
            PollIntervalSeconds = pollIntervalSeconds,
            LastError = null
        });

        if (_settingsStore is not null)
        {
            var current = _settingsStore.GetSettings();
            _settingsStore.UpdateSettings(current with
            {
                Dump1090 = new Dump1090Settings(request.Host, request.Port, jsonPath, pollIntervalSeconds, AutoConnect: true)
            });
        }

        var loopCts = new CancellationTokenSource();
        _loopCts = loopCts;
        _loopTask = Task.Run(() => RunPollLoopAsync(request.Host, request.Port, jsonPath, pollIntervalSeconds, loopCts.Token));

        return Task.FromResult(GetStatus());
    }

    public Task<Dump1090ConnectionSnapshot> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        StopLoop();
        SetSnapshot(_snapshot with { State = SourceState.Offline });
        AddDiagnostic("Info", "DUMP1090_DISCONNECTED", "Disconnected from dump1090.");
        return Task.FromResult(GetStatus());
    }

    private async Task RunPollLoopAsync(string host, int port, string jsonPath, int pollIntervalSeconds, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollIntervalSeconds));

        try
        {
            await PollOnceAsync(host, port, jsonPath, cancellationToken);

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await PollOnceAsync(host, port, jsonPath, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disconnecting.
        }
    }

    private async Task PollOnceAsync(string host, int port, string jsonPath, CancellationToken cancellationToken)
    {
        var uri = new Uri($"http://{host}:{port}{jsonPath}");

        try
        {
            var client = _httpClientFactory.CreateClient("dump1090");
            var response = await client.GetFromJsonAsync<Dump1090AircraftJsonResponse>(uri, JsonOptions, cancellationToken);

            var (accepted, rejected) = ApplyAircraftResponse(response, uri);

            long totalAccepted, totalRejected;

            lock (_gate)
            {
                totalAccepted = _snapshot.AcceptedRecords + accepted;
                totalRejected = _snapshot.RejectedRecords + rejected;
            }

            SetSnapshot(_snapshot with
            {
                State = SourceState.Online,
                AcceptedRecords = totalAccepted,
                RejectedRecords = totalRejected,
                LastPolledAtUtc = _clock.UtcNow,
                LastError = null
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            SetSnapshot(_snapshot with
            {
                State = SourceState.Degraded,
                LastError = exception.Message
            });
            AddDiagnostic("Warning", "DUMP1090_POLL_FAILED", $"Failed to poll dump1090 at {uri}: {exception.Message}");
        }
    }

    private (int Accepted, int Rejected) ApplyAircraftResponse(Dump1090AircraftJsonResponse? response, Uri uri)
    {
        var accepted = 0;
        var rejected = 0;

        if (response?.Aircraft is null)
        {
            return (accepted, rejected);
        }

        var receivedAtUtc = _clock.UtcNow;

        foreach (var entry in response.Aircraft)
        {
            var rawReference = $"{uri}#{entry.Hex ?? "unknown"}";

            if (entry.Hex is not { Length: 6 } hex)
            {
                rejected++;
                AddDiagnostic("Warning", "IMPORT_SCHEMA_MISMATCH", "dump1090 aircraft entry is missing a six-character hex address and was quarantined.", rawReference);
                continue;
            }

            hex = hex.ToUpperInvariant();

            if (entry.SeenPos is > 900)
            {
                rejected++;
                AddDiagnostic("Warning", "IMPORT_ADSB_STALE", $"dump1090 aircraft {hex} position is stale and was quarantined.", rawReference);
                continue;
            }

            if (!IsValidCoordinate(entry.Lat, entry.Lon))
            {
                rejected++;
                AddDiagnostic("Warning", "IMPORT_ADSB_INVALID_POSITION", $"dump1090 aircraft {hex} has invalid coordinates and was quarantined.", rawReference);
                continue;
            }

            _aircraftRegistryLookup.TryLookup(hex, out var registryEntry);

            var updatedAtUtc = receivedAtUtc - TimeSpan.FromSeconds(entry.SeenPos ?? 0);
            var track = new AircraftTrackSnapshot(
                hex,
                updatedAtUtc,
                entry.Lat,
                entry.Lon,
                ParseAltBaro(entry.AltBaroRaw),
                ImportId,
                ParserConfidence.Observed,
                string.IsNullOrWhiteSpace(entry.Flight) ? null : entry.Flight.Trim(),
                entry.Gs,
                entry.Track,
                "Imported ADS-B/dump1090 (network)",
                $"adsb-{hex}",
                false,
                new IngestionProvenance(
                    IngestionPath.ImportedDecodedData,
                    ImportId,
                    "dump1090 network connection",
                    "dump1090",
                    "application/json; domain=dump1090-aircraft",
                    AdapterName,
                    AdapterVersion,
                    response.Now is { } now ? DateTimeOffset.UnixEpoch.AddSeconds(now) : null,
                    receivedAtUtc,
                    rawReference),
                EmitterCategory: string.IsNullOrWhiteSpace(entry.Category) ? null : entry.Category,
                AircraftType: string.IsNullOrWhiteSpace(entry.AircraftType) ? registryEntry.IcaoTypeCode : entry.AircraftType,
                Registration: string.IsNullOrWhiteSpace(entry.Registration) ? registryEntry.Registration : entry.Registration.Trim(),
                OperatorName: registryEntry.Operator,
                IsMilitary: registryEntry.IsMilitary);

            if (_aircraftTrackStore.Upsert(track))
            {
                accepted++;
                AddDiagnostic("Info", "IMPORT_RECORD_ACCEPTED", $"Imported dump1090 aircraft {hex}.", rawReference);
            }
            else
            {
                rejected++;
                AddDiagnostic("Warning", "IMPORT_OUT_OF_ORDER", $"dump1090 aircraft {hex} update was older than the current track and was quarantined.", rawReference);
            }
        }

        return (accepted, rejected);
    }

    private static double? ParseAltBaro(JsonElement? raw)
    {
        return raw is { ValueKind: JsonValueKind.Number } element && element.TryGetDouble(out var value) ? value : null;
    }

    private static bool IsValidCoordinate(double? latitude, double? longitude)
    {
        return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    private void StopLoop()
    {
        var cts = _loopCts;
        var task = _loopTask;
        _loopCts = null;
        _loopTask = null;

        if (cts is null)
        {
            return;
        }

        cts.Cancel();

        try
        {
            task?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping the poll loop.
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void SetSnapshot(Dump1090ConnectionSnapshot snapshot)
    {
        lock (_gate)
        {
            _snapshot = snapshot;
        }

        ConnectionStateChanged?.Invoke(this, snapshot);
    }

    private void AddDiagnostic(string severity, string code, string message, string? rawReference = null)
    {
        var diagnostic = new ImportDiagnostic(
            $"dump1090-diagnostic-{Interlocked.Increment(ref _diagnosticSequence)}",
            _clock.UtcNow,
            ImportId,
            severity,
            code,
            message,
            rawReference);

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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public void Dispose()
    {
        StopLoop();
    }
}
