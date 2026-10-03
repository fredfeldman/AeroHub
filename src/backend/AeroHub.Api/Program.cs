using AeroHub.Contracts;
using AeroHub.Api.Hubs;
using AeroHub.Api.Services;
using AeroHub.Core;
using AeroHub.Decoders.Acars;
using AeroHub.Infrastructure;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var startedAtUtc = DateTimeOffset.UtcNow;

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(Path.Combine(AppContext.BaseDirectory, "data", "store")));
builder.Services.AddSingleton<IRecordStore>(_ => new SqliteRecordStore(Path.Combine(AppContext.BaseDirectory, "data", "store", "aerohub.db")));
builder.Services.AddSingleton<IMessageBus, InMemoryMessageBus>();
builder.Services.AddSingleton<IAcarsMessageParser, AcarsMessageParser>();
builder.Services.AddSingleton<IFixtureReplayService, FixtureReplayService>();
builder.Services.AddSingleton<IAircraftTrackStore, InMemoryAircraftTrackStore>();
builder.Services.AddSingleton<IRemoteIdObservationStore, InMemoryRemoteIdObservationStore>();
builder.Services.AddSingleton<IAircraftRegistryLookup, AircraftRegistryLookup>();
builder.Services.AddSingleton<ISondeTrackStore, InMemorySondeTrackStore>();
builder.Services.AddSingleton<INavaidService, StaticNavaidService>();
builder.Services.AddHttpClient("planespotters", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.Referrer = new Uri("https://www.planespotters.net/");
});
builder.Services.AddSingleton<IAircraftPhotoLookup, PlanespottersPhotoLookup>();
builder.Services.AddSingleton<IImportManager, FileNdjsonImportManager>();
builder.Services.AddSingleton<ISyntheticRfStreamService, SyntheticRfStreamService>();
builder.Services.AddSingleton<IExternalDecoderProcessManager, ExternalDecoderProcessManager>();
builder.Services.AddSingleton<IHardwareSourceManager, SimulatedHardwareSourceManager>();
builder.Services.AddHttpClient("dump1090", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<IDump1090NetworkImportManager, Dump1090NetworkImportManager>();
builder.Services.AddSingleton<IFeederManager, FlightAwareFeederManager>();
builder.Services.AddSingleton<IWefaxReplayService, SyntheticWefaxReplayService>();
builder.Services.AddHostedService<SignalRMessageBroadcaster>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(origin =>
                origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)
                || origin.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (IHostEnvironment environment) =>
    new HealthStatus(
        "AeroHub.Api",
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        environment.EnvironmentName,
        startedAtUtc,
        DateTimeOffset.UtcNow,
        [
            "native-decoder-shell",
            "decoded-data-import-shell",
            "health-api",
            "message-bus",
            "fixture-replay",
            "signalr-events",
            "ndjson-import",
            "synthetic-rf-stream",
            "adsb-track-import",
            "external-decoder-supervision",
            "wefax-synthetic-replay",
            "jsonl-record-store",
            "simulated-hardware-sources",
            "radiosonde-track-import",
            "navaids-catalog",
            "flightaware-data-feeder",
            "decoder-settings-persistence"
        ]))
.WithName("GetHealth")
.WithOpenApi();

app.MapGet("/api/diagnostics/operator", (IHostEnvironment environment, IImportManager importManager, ISyntheticRfStreamService syntheticRfStreamService, IRecordStore recordStore) =>
{
    var importStates = importManager.GetImports();
    var streamMetrics = syntheticRfStreamService.GetMetrics();
    var storageSnapshot = recordStore.GetSnapshot(DateTimeOffset.UtcNow);
    var warnings = importManager.GetDiagnostics(8)
        .Select(diagnostic => diagnostic.Code)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(8)
        .ToArray();
    var activeImports = importStates.Count(import => import.State is SourceState.Online or SourceState.Starting);
    var systemHealth = activeImports == 0 && warnings.Length == 0 && streamMetrics.QueueDepth <= 10 ? "Healthy" : "Degraded";

    return new OperatorDiagnosticsSnapshot(
        "AeroHub.Api",
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        environment.EnvironmentName,
        startedAtUtc,
        DateTimeOffset.UtcNow,
        1,
        activeImports,
        streamMetrics.QueueDepth,
        warnings.Length,
        systemHealth,
        storageSnapshot.BytesUsed,
        warnings);
})
.WithName("GetOperatorDiagnostics")
.WithOpenApi();

app.MapGet("/api/messages/recent", (IMessageBus messageBus, int limit = 50) =>
    messageBus.GetRecent(limit))
.WithName("GetRecentMessages")
.WithOpenApi();

app.MapGet("/api/sources", (ISettingsStore settingsStore) =>
    settingsStore.GetSettings().FrequencyProfiles.Select(profile => new SignalSourceSnapshot(
        profile.Id,
        profile.Name,
        SourceState.Offline,
        "LocalFrequencyProfile",
        profile.FrequencyMHz,
        profile.Tags)))
.WithName("GetSources")
.WithOpenApi();

app.MapGet("/api/settings", (ISettingsStore settingsStore) =>
    settingsStore.GetSettings())
.WithName("GetSettings")
.WithOpenApi();

app.MapPut("/api/settings", (DecoderSettings request, ISettingsStore settingsStore) =>
    settingsStore.UpdateSettings(request))
.WithName("UpdateSettings")
.WithOpenApi();

app.MapGet("/api/imports", (IImportManager importManager) =>
    importManager.GetImports())
.WithName("GetImports")
.WithOpenApi();

app.MapGet("/api/imports/diagnostics", (IImportManager importManager, int limit = 50) =>
    importManager.GetDiagnostics(limit))
.WithName("GetImportDiagnostics")
.WithOpenApi();

app.MapGet("/api/aircraft", (IAircraftTrackStore aircraftTrackStore) =>
    aircraftTrackStore.GetCurrentTracks())
.WithName("GetAircraftTracks")
.WithOpenApi();

app.MapGet("/api/remote-id/observations", (IRemoteIdObservationStore observationStore, int limit = 100) =>
    observationStore.GetRecent(limit))
.WithName("GetRemoteIdObservations")
.WithOpenApi();

app.MapGet("/api/aircraft/{aircraftIdentifier}/history", (string aircraftIdentifier, IRecordStore recordStore, int limit = 300) =>
    recordStore.GetPositionHistory(aircraftIdentifier, limit))
.WithName("GetAircraftPositionHistory")
.WithOpenApi();

app.MapGet("/api/aircraft/{aircraftIdentifier}/photo", async (string aircraftIdentifier, IAircraftPhotoLookup photoLookup, CancellationToken cancellationToken) =>
{
    var photo = await photoLookup.GetPhotoAsync(aircraftIdentifier, cancellationToken);
    return photo is null ? Results.NotFound() : Results.Ok(photo);
})
.WithName("GetAircraftPhoto")
.WithOpenApi();

app.MapGet("/api/sondes", (ISondeTrackStore sondeTrackStore) =>
    sondeTrackStore.GetCurrentTracks())
.WithName("GetSondeTracks")
.WithOpenApi();

app.MapGet("/api/sondes/{serial}/history", (string serial, IRecordStore recordStore, int limit = 300) =>
    recordStore.GetSondePositionHistory(serial, limit))
.WithName("GetSondePositionHistory")
.WithOpenApi();

app.MapGet("/api/navaids", (INavaidService navaidService, double? maxDistanceMiles = 200) =>
    navaidService.GetNavaids(maxDistanceMiles))
.WithName("GetNavaids")
.WithOpenApi();

app.MapGet("/api/feeders", (IFeederManager feederManager) =>
    feederManager.GetFeeders())
.WithName("GetFeeders")
.WithOpenApi();

app.MapGet("/api/feeders/diagnostics", (IFeederManager feederManager, int limit = 50) =>
    feederManager.GetDiagnostics(limit))
.WithName("GetFeederDiagnostics")
.WithOpenApi();

app.MapPost("/api/feeders/start", async (FeederStartRequest request, IFeederManager feederManager, CancellationToken cancellationToken) =>
    await feederManager.StartFeederAsync(request, cancellationToken))
.WithName("StartFeeder")
.WithOpenApi();

app.MapPost("/api/feeders/{feederId}/stop", async (string feederId, IFeederManager feederManager, CancellationToken cancellationToken) =>
    await feederManager.StopFeederAsync(feederId, cancellationToken))
.WithName("StopFeeder")
.WithOpenApi();

app.MapPost("/api/feeders/{feederId}/test", async (string feederId, IFeederManager feederManager, CancellationToken cancellationToken) =>
    await feederManager.SendTestFrameAsync(feederId, cancellationToken))
.WithName("SendFeederTestFrame")
.WithOpenApi();

app.MapGet("/api/external-decoders", (IExternalDecoderProcessManager processManager) =>
    processManager.GetProcesses())
.WithName("GetExternalDecoders")
.WithOpenApi();

app.MapGet("/api/external-decoders/diagnostics", (IExternalDecoderProcessManager processManager, int limit = 50) =>
    processManager.GetDiagnostics(limit))
.WithName("GetExternalDecoderDiagnostics")
.WithOpenApi();

app.MapPost("/api/external-decoders/{processId}/simulate/{scenario}", async (string processId, string scenario, IExternalDecoderProcessManager processManager, CancellationToken cancellationToken) =>
    await processManager.SimulateAsync(processId, scenario, cancellationToken))
.WithName("SimulateExternalDecoder")
.WithOpenApi();

app.MapPost("/api/imports/{importId}/start", async (string importId, IImportManager importManager, CancellationToken cancellationToken) =>
    await importManager.StartImportAsync(importId, cancellationToken))
.WithName("StartImport")
.WithOpenApi();

app.MapGet("/api/imports/dump1090/status", (IDump1090NetworkImportManager dump1090ImportManager) =>
    dump1090ImportManager.GetStatus())
.WithName("GetDump1090Status")
.WithOpenApi();

app.MapGet("/api/imports/dump1090/diagnostics", (IDump1090NetworkImportManager dump1090ImportManager, int limit = 50) =>
    dump1090ImportManager.GetDiagnostics(limit))
.WithName("GetDump1090Diagnostics")
.WithOpenApi();

app.MapPost("/api/imports/dump1090/connect", async (Dump1090ConnectionRequest request, IDump1090NetworkImportManager dump1090ImportManager, CancellationToken cancellationToken) =>
    await dump1090ImportManager.ConnectAsync(request, cancellationToken))
.WithName("ConnectDump1090")
.WithOpenApi();

app.MapPost("/api/imports/dump1090/disconnect", async (IDump1090NetworkImportManager dump1090ImportManager, CancellationToken cancellationToken) =>
    await dump1090ImportManager.DisconnectAsync(cancellationToken))
.WithName("DisconnectDump1090")
.WithOpenApi();

app.MapGet("/api/decoders", () => new DecoderStateSnapshot[]
{
    new(
        "acars-message-core",
        "ACARS message core",
        SourceState.Offline,
        [AviationMessageKind.Acars],
        false)
})
.WithName("GetDecoders")
.WithOpenApi();

app.MapPost("/api/fixtures/replay/acars", async (IFixtureReplayService fixtureReplay, CancellationToken cancellationToken) =>
    await fixtureReplay.ReplayAcarsAsync(cancellationToken))
.WithName("ReplayAcarsFixture")
.WithOpenApi();

app.MapGet("/api/streams/synthetic/metrics", (ISyntheticRfStreamService syntheticRfStreamService) =>
    syntheticRfStreamService.GetMetrics())
.WithName("GetSyntheticStreamMetrics")
.WithOpenApi();

app.MapPost("/api/streams/synthetic/replay", async (ISyntheticRfStreamService syntheticRfStreamService, int speedMultiplier = 1, CancellationToken cancellationToken = default) =>
    await syntheticRfStreamService.ReplayAsync(speedMultiplier, cancellationToken))
.WithName("ReplaySyntheticStream")
.WithOpenApi();

app.MapGet("/api/wefax/state", (IWefaxReplayService wefaxReplayService) =>
    wefaxReplayService.GetState())
.WithName("GetWefaxState")
.WithOpenApi();

app.MapPost("/api/wefax/replay", async (IWefaxReplayService wefaxReplayService, int lineCount = 80, bool partial = false, CancellationToken cancellationToken = default) =>
    await wefaxReplayService.ReplayAsync(lineCount, partial, cancellationToken))
.WithName("ReplayWefax")
.WithOpenApi();

app.MapPost("/api/wefax/controls", (IWefaxReplayService wefaxReplayService, bool isInverted = false, double slantCorrection = 0) =>
    wefaxReplayService.SetControls(isInverted, slantCorrection))
.WithName("SetWefaxControls")
.WithOpenApi();

app.MapGet("/api/storage", (IRecordStore recordStore, IClock clock) =>
    recordStore.GetSnapshot(clock.UtcNow))
.WithName("GetStorageSnapshot")
.WithOpenApi();

app.MapPost("/api/storage/retention/apply", (IRecordStore recordStore, IClock clock, int messageDays = 30, int trackDays = 30, int diagnosticDays = 14) =>
    recordStore.ApplyRetention(new StorageRetentionPolicy(messageDays, trackDays, diagnosticDays), clock.UtcNow))
.WithName("ApplyStorageRetention")
.WithOpenApi();

app.MapPost("/api/storage/export", (IRecordStore recordStore, IClock clock, string exportName = "manual-snapshot") =>
    recordStore.ExportSnapshot(exportName, clock.UtcNow))
.WithName("ExportStorageSnapshot")
.WithOpenApi();

app.MapGet("/api/hardware-sources", (IHardwareSourceManager hardwareSourceManager) =>
    hardwareSourceManager.GetSources())
.WithName("GetHardwareSources")
.WithOpenApi();

app.MapGet("/api/hardware-sources/diagnostics", (IHardwareSourceManager hardwareSourceManager, int limit = 50) =>
    hardwareSourceManager.GetDiagnostics(limit))
.WithName("GetHardwareSourceDiagnostics")
.WithOpenApi();

app.MapPost("/api/hardware-sources/start", async (HardwareSourceStartRequest request, IHardwareSourceManager hardwareSourceManager, CancellationToken cancellationToken) =>
    await hardwareSourceManager.StartAsync(request, cancellationToken))
.WithName("StartHardwareSource")
.WithOpenApi();

app.MapPost("/api/hardware-sources/{sourceId}/stop", async (string sourceId, IHardwareSourceManager hardwareSourceManager, CancellationToken cancellationToken) =>
    await hardwareSourceManager.StopAsync(sourceId, cancellationToken))
.WithName("StopHardwareSource")
.WithOpenApi();

app.MapPost("/api/hardware-sources/{sourceId}/simulate/{scenario}", async (string sourceId, string scenario, IHardwareSourceManager hardwareSourceManager, CancellationToken cancellationToken) =>
    await hardwareSourceManager.SimulateAsync(sourceId, scenario, cancellationToken))
.WithName("SimulateHardwareSource")
.WithOpenApi();

app.MapHub<AeroHubHub>("/hubs/aerohub");

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
