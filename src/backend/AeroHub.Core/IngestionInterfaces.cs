using AeroHub.Contracts;

namespace AeroHub.Core;

public interface ISignalSource
{
    SignalSourceSnapshot Snapshot { get; }
}

public interface IDecodedDataSource
{
    DecodedDataSourceSnapshot Snapshot { get; }
}

public interface IImportAdapter
{
    string AdapterName { get; }

    string SourceFormat { get; }
}

public interface IImportManager
{
    event EventHandler<ImportDiagnostic>? DiagnosticReceived;

    event EventHandler<ImportStateSnapshot>? ImportStateChanged;

    IReadOnlyList<ImportStateSnapshot> GetImports();

    IReadOnlyList<ImportDiagnostic> GetDiagnostics(int limit);

    Task<ImportReplayResult> StartImportAsync(string importId, CancellationToken cancellationToken = default);
}

public interface ISpectrumFrameProvider
{
    string SourceId { get; }
}

public interface IDecoderModule
{
    DecoderStateSnapshot Snapshot { get; }
}

public interface IStreamingDecoder : IDecoderModule
{
}

public interface IBatchDecoder : IDecoderModule
{
}

public interface IDecoderManager
{
    IReadOnlyList<DecoderStateSnapshot> GetDecoders();
}

public interface IAircraftTrackStore
{
    event EventHandler<AircraftTrackSnapshot>? TrackUpdated;

    IReadOnlyList<AircraftTrackSnapshot> GetCurrentTracks();

    bool Upsert(AircraftTrackSnapshot track);
}

public interface IRemoteIdObservationStore
{
    event EventHandler<RemoteIdObservation>? ObservationReceived;

    IReadOnlyList<RemoteIdObservation> GetRecent(int limit);

    void Add(RemoteIdObservation observation);
}

public interface ISondeTrackStore
{
    event EventHandler<SondeTelemetrySnapshot>? TrackUpdated;

    IReadOnlyList<SondeTelemetrySnapshot> GetCurrentTracks();

    bool Upsert(SondeTelemetrySnapshot track);
}

public readonly record struct AircraftRegistryEntry(string? Registration, string? IcaoTypeCode, string? Operator, bool IsMilitary = false);

public interface IAircraftRegistryLookup
{
    bool TryLookup(string hexAddress, out AircraftRegistryEntry entry);
}

public sealed record AircraftPhotoInfo(
    string ThumbnailUrl,
    string? LargeUrl,
    string? PhotographerName,
    string? SourceLink);

public interface IAircraftPhotoLookup
{
    Task<AircraftPhotoInfo?> GetPhotoAsync(string hexAddress, CancellationToken cancellationToken = default);
}

public interface IExternalDecoderProcessManager
{
    event EventHandler<ExternalDecoderDiagnostic>? DiagnosticReceived;

    event EventHandler<ExternalDecoderProcessSnapshot>? ProcessStateChanged;

    IReadOnlyList<ExternalDecoderProcessSnapshot> GetProcesses();

    IReadOnlyList<ExternalDecoderDiagnostic> GetDiagnostics(int limit);

    Task<ExternalDecoderSimulationResult> SimulateAsync(string processId, string scenario, CancellationToken cancellationToken = default);
}

public interface IDump1090NetworkImportManager
{
    event EventHandler<ImportDiagnostic>? DiagnosticReceived;

    event EventHandler<Dump1090ConnectionSnapshot>? ConnectionStateChanged;

    Dump1090ConnectionSnapshot GetStatus();

    IReadOnlyList<ImportDiagnostic> GetDiagnostics(int limit);

    Task<Dump1090ConnectionSnapshot> ConnectAsync(Dump1090ConnectionRequest request, CancellationToken cancellationToken = default);

    Task<Dump1090ConnectionSnapshot> DisconnectAsync(CancellationToken cancellationToken = default);
}