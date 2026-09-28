using AeroHub.Api.Hubs;
using AeroHub.Contracts;
using AeroHub.Core;
using Microsoft.AspNetCore.SignalR;

namespace AeroHub.Api.Services;

public sealed class SignalRMessageBroadcaster(
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
    ISettingsStore settingsStore,
    IHubContext<AeroHubHub> hubContext,
    ILogger<SignalRMessageBroadcaster> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        messageBus.MessageReceived += OnMessageReceived;
        importManager.DiagnosticReceived += OnImportDiagnosticReceived;
        importManager.ImportStateChanged += OnImportStateChanged;
        syntheticRfStreamService.SpectrumFrameProduced += OnSpectrumFrameProduced;
        syntheticRfStreamService.WaterfallRowsProduced += OnWaterfallRowsProduced;
        syntheticRfStreamService.MetricsUpdated += OnStreamMetricsUpdated;
        aircraftTrackStore.TrackUpdated += OnAircraftTrackUpdated;
        sondeTrackStore.TrackUpdated += OnSondeTrackUpdated;
        externalDecoderProcessManager.DiagnosticReceived += OnExternalDecoderDiagnosticReceived;
        externalDecoderProcessManager.ProcessStateChanged += OnExternalDecoderProcessStateChanged;
        hardwareSourceManager.DiagnosticReceived += OnHardwareSourceDiagnosticReceived;
        hardwareSourceManager.SourceStateChanged += OnHardwareSourceStateChanged;
        wefaxReplayService.LinesPublished += OnWefaxLinesPublished;
        wefaxReplayService.StateChanged += OnWefaxStateChanged;
        dump1090ImportManager.DiagnosticReceived += OnDump1090DiagnosticReceived;
        dump1090ImportManager.ConnectionStateChanged += OnDump1090ConnectionStateChanged;
        feederManager.DiagnosticReceived += OnFeederDiagnosticReceived;
        feederManager.FeederStateChanged += OnFeederStateChanged;
        settingsStore.SettingsChanged += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        messageBus.MessageReceived -= OnMessageReceived;
        importManager.DiagnosticReceived -= OnImportDiagnosticReceived;
        importManager.ImportStateChanged -= OnImportStateChanged;
        syntheticRfStreamService.SpectrumFrameProduced -= OnSpectrumFrameProduced;
        syntheticRfStreamService.WaterfallRowsProduced -= OnWaterfallRowsProduced;
        syntheticRfStreamService.MetricsUpdated -= OnStreamMetricsUpdated;
        aircraftTrackStore.TrackUpdated -= OnAircraftTrackUpdated;
        sondeTrackStore.TrackUpdated -= OnSondeTrackUpdated;
        externalDecoderProcessManager.DiagnosticReceived -= OnExternalDecoderDiagnosticReceived;
        externalDecoderProcessManager.ProcessStateChanged -= OnExternalDecoderProcessStateChanged;
        hardwareSourceManager.DiagnosticReceived -= OnHardwareSourceDiagnosticReceived;
        hardwareSourceManager.SourceStateChanged -= OnHardwareSourceStateChanged;
        wefaxReplayService.LinesPublished -= OnWefaxLinesPublished;
        wefaxReplayService.StateChanged -= OnWefaxStateChanged;
        dump1090ImportManager.DiagnosticReceived -= OnDump1090DiagnosticReceived;
        dump1090ImportManager.ConnectionStateChanged -= OnDump1090ConnectionStateChanged;
        feederManager.DiagnosticReceived -= OnFeederDiagnosticReceived;
        feederManager.FeederStateChanged -= OnFeederStateChanged;
        settingsStore.SettingsChanged -= OnSettingsChanged;
        return Task.CompletedTask;
    }

    private void OnMessageReceived(object? sender, NormalizedAviationMessage message)
    {
        _ = BroadcastAsync("message.received", message, message.Id);
    }

    private void OnImportDiagnosticReceived(object? sender, ImportDiagnostic diagnostic)
    {
        _ = BroadcastAsync("import.diagnostic", diagnostic, diagnostic.Id);
    }

    private void OnImportStateChanged(object? sender, ImportStateSnapshot snapshot)
    {
        _ = BroadcastAsync("import.updated", snapshot, snapshot.Id);
    }

    private void OnSpectrumFrameProduced(object? sender, SpectrumFrame frame)
    {
        _ = BroadcastAsync("spectrum.frame", frame, $"{frame.SourceId}:{frame.Sequence}");
    }

    private void OnWaterfallRowsProduced(object? sender, WaterfallRows rows)
    {
        _ = BroadcastAsync("waterfall.rows", rows, $"{rows.SourceId}:{rows.FirstSequence}");
    }

    private void OnStreamMetricsUpdated(object? sender, StreamMetricsSnapshot metrics)
    {
        _ = BroadcastAsync("stream.metrics", metrics, metrics.SourceId);
    }

    private void OnAircraftTrackUpdated(object? sender, AircraftTrackSnapshot track)
    {
        _ = BroadcastAsync("aircraft.updated", track, track.AircraftIdentifier);
    }

    private void OnSondeTrackUpdated(object? sender, SondeTelemetrySnapshot track)
    {
        _ = BroadcastAsync("sonde.updated", track, track.Serial);
    }

    private void OnExternalDecoderDiagnosticReceived(object? sender, ExternalDecoderDiagnostic diagnostic)
    {
        _ = BroadcastAsync("external.diagnostic", diagnostic, diagnostic.Id);
    }

    private void OnExternalDecoderProcessStateChanged(object? sender, ExternalDecoderProcessSnapshot snapshot)
    {
        _ = BroadcastAsync("external.updated", snapshot, snapshot.Id);
    }

    private void OnHardwareSourceDiagnosticReceived(object? sender, HardwareSourceDiagnostic diagnostic)
    {
        _ = BroadcastAsync("hardware.diagnostic", diagnostic, diagnostic.Id);
    }

    private void OnHardwareSourceStateChanged(object? sender, HardwareSourceSnapshot snapshot)
    {
        _ = BroadcastAsync("hardware.updated", snapshot, snapshot.Id);
    }

    private void OnWefaxLinesPublished(object? sender, WefaxLineBatch batch)
    {
        _ = BroadcastAsync("wefax.lines", batch, $"{batch.SourceId}:{batch.FirstSequence}");
    }

    private void OnWefaxStateChanged(object? sender, WefaxDecoderState state)
    {
        _ = BroadcastAsync("wefax.state", state, state.SourceId);
    }

    private void OnDump1090DiagnosticReceived(object? sender, ImportDiagnostic diagnostic)
    {
        _ = BroadcastAsync("dump1090.diagnostic", diagnostic, diagnostic.Id);
    }

    private void OnDump1090ConnectionStateChanged(object? sender, Dump1090ConnectionSnapshot snapshot)
    {
        _ = BroadcastAsync("dump1090.updated", snapshot, snapshot.ImportId);
    }

    private void OnFeederDiagnosticReceived(object? sender, FeederDiagnostic diagnostic)
    {
        _ = BroadcastAsync("feeder.diagnostic", diagnostic, diagnostic.Id);
    }

    private void OnFeederStateChanged(object? sender, FeederStatusSnapshot snapshot)
    {
        _ = BroadcastAsync("feeder.updated", snapshot, snapshot.Id);
    }

    private void OnSettingsChanged(object? sender, DecoderSettings settings)
    {
        _ = BroadcastAsync("settings.updated", settings, "decoder-settings");
    }

    private async Task BroadcastAsync<T>(string eventName, T payload, string payloadId)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(eventName, payload);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to broadcast {EventName} payload {PayloadId}.", eventName, payloadId);
        }
    }
}