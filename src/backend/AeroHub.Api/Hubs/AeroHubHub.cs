using AeroHub.Core;
using Microsoft.AspNetCore.SignalR;

namespace AeroHub.Api.Hubs;

public sealed class AeroHubHub(
    IMessageBus messageBus,
    IImportManager importManager,
    ISyntheticRfStreamService syntheticRfStreamService,
    IAircraftTrackStore aircraftTrackStore,
    ISondeTrackStore sondeTrackStore,
    IExternalDecoderProcessManager externalDecoderProcessManager,
    IHardwareSourceManager hardwareSourceManager,
    IWefaxReplayService wefaxReplayService,
    IDump1090NetworkImportManager dump1090ImportManager,
    IFeederManager feederManager,
    ISettingsStore settingsStore) : Hub
{
    public Task GetRecentMessages(int limit = 50)
    {
        return Clients.Caller.SendAsync("message.snapshot", messageBus.GetRecent(limit));
    }

    public Task GetRecentImportDiagnostics(int limit = 50)
    {
        return Clients.Caller.SendAsync("import.diagnostic.snapshot", importManager.GetDiagnostics(limit));
    }

    public Task GetStreamMetrics()
    {
        return Clients.Caller.SendAsync("stream.metrics", syntheticRfStreamService.GetMetrics());
    }

    public Task GetAircraftTracks()
    {
        return Clients.Caller.SendAsync("aircraft.snapshot", aircraftTrackStore.GetCurrentTracks());
    }

    public Task GetSondeTracks()
    {
        return Clients.Caller.SendAsync("sonde.snapshot", sondeTrackStore.GetCurrentTracks());
    }

    public Task GetExternalDecoderProcesses()
    {
        return Clients.Caller.SendAsync("external.snapshot", externalDecoderProcessManager.GetProcesses());
    }

    public Task GetExternalDecoderDiagnostics(int limit = 50)
    {
        return Clients.Caller.SendAsync("external.diagnostic.snapshot", externalDecoderProcessManager.GetDiagnostics(limit));
    }

    public Task GetWefaxState()
    {
        return Clients.Caller.SendAsync("wefax.state", wefaxReplayService.GetState());
    }

    public Task GetHardwareSources()
    {
        return Clients.Caller.SendAsync("hardware.snapshot", hardwareSourceManager.GetSources());
    }

    public Task GetHardwareSourceDiagnostics(int limit = 50)
    {
        return Clients.Caller.SendAsync("hardware.diagnostic.snapshot", hardwareSourceManager.GetDiagnostics(limit));
    }

    public Task GetDump1090Status()
    {
        return Clients.Caller.SendAsync("dump1090.snapshot", dump1090ImportManager.GetStatus());
    }

    public Task GetDump1090Diagnostics(int limit = 50)
    {
        return Clients.Caller.SendAsync("dump1090.diagnostic.snapshot", dump1090ImportManager.GetDiagnostics(limit));
    }

    public Task GetFeeders()
    {
        return Clients.Caller.SendAsync("feeder.snapshot", feederManager.GetFeeders());
    }

    public Task GetFeederDiagnostics(int limit = 50)
    {
        return Clients.Caller.SendAsync("feeder.diagnostic.snapshot", feederManager.GetDiagnostics(limit));
    }

    public Task GetSettings()
    {
        return Clients.Caller.SendAsync("settings.snapshot", settingsStore.GetSettings());
    }
}