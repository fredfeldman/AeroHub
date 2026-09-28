using AeroHub.Contracts;

namespace AeroHub.Core;

public interface IRecordStore
{
    void SaveMessage(NormalizedAviationMessage message);

    IReadOnlyList<NormalizedAviationMessage> GetRecentMessages(int limit);

    bool SaveTrack(AircraftTrackSnapshot track);

    IReadOnlyList<AircraftTrackSnapshot> GetCurrentTracks(DateTimeOffset utcNow, TimeSpan staleAfter);

    void SaveImportDiagnostic(ImportDiagnostic diagnostic);

    IReadOnlyList<ImportDiagnostic> GetImportDiagnostics(int limit);

    void SavePosition(AircraftPositionSample sample);

    IReadOnlyList<AircraftPositionSample> GetPositionHistory(string aircraftIdentifier, int limit);

    bool SaveSondeTrack(SondeTelemetrySnapshot track);

    IReadOnlyList<SondeTelemetrySnapshot> GetCurrentSondeTracks(DateTimeOffset utcNow, TimeSpan staleAfter);

    void SaveSondePosition(SondePositionSample sample);

    IReadOnlyList<SondePositionSample> GetSondePositionHistory(string serial, int limit);

    StorageSnapshot GetSnapshot(DateTimeOffset checkedAtUtc);

    StorageRetentionResult ApplyRetention(StorageRetentionPolicy policy, DateTimeOffset utcNow);

    StorageExportResult ExportSnapshot(string exportName, DateTimeOffset exportedAtUtc);
}