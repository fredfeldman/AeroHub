using System.Text.Json;
using System.Text.Json.Serialization;
using AeroHub.Contracts;
using AeroHub.Core;
using AeroHub.Decoders.Acars;

namespace AeroHub.Infrastructure;

public sealed class FileNdjsonImportManager(
    IMessageBus messageBus,
    IClock clock,
    IAcarsMessageParser acarsMessageParser,
    IAircraftTrackStore aircraftTrackStore,
    IAircraftRegistryLookup aircraftRegistryLookup,
    IRecordStore? recordStore = null,
    ISondeTrackStore? sondeTrackStore = null,
    IRemoteIdObservationStore? remoteIdObservationStore = null) : IImportManager
{
    private const int MaximumDiagnostics = 200;
    private const int MaximumRecordBytes = 4096;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly object _gate = new();
    private readonly List<ImportDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, ImportStateSnapshot> _imports = new(StringComparer.OrdinalIgnoreCase)
    {
        ["local-acars-ndjson"] = new ImportStateSnapshot(
            "local-acars-ndjson",
            "Local ACARS NDJSON sample",
            SourceState.Offline,
            "application/x-ndjson; domain=acars",
            0,
            0,
            null),
        ["local-adsb-readsb-json"] = new ImportStateSnapshot(
            "local-adsb-readsb-json",
            "Local ADS-B readsb sample",
            SourceState.Offline,
            "application/x-ndjson; domain=adsb-readsb",
            0,
            0,
            null),
        ["local-remote-id-json"] = new ImportStateSnapshot(
            "local-remote-id-json",
            "Local Remote ID commercial drone sample",
            SourceState.Offline,
            "application/x-ndjson; domain=remote-id",
            0,
            0,
            null),
        ["sample-dumphfdl-json"] = new ImportStateSnapshot(
            "sample-dumphfdl-json",
            "Sample dumphfdl decoded output",
            SourceState.Offline,
            "application/x-ndjson; domain=dumphfdl",
            0,
            0,
            null),
        ["sample-dumpvdl2-json"] = new ImportStateSnapshot(
            "sample-dumpvdl2-json",
            "Sample dumpvdl2 decoded output",
            SourceState.Offline,
            "application/x-ndjson; domain=dumpvdl2",
            0,
            0,
            null),
        ["sample-satcom-json"] = new ImportStateSnapshot(
            "sample-satcom-json",
            "Sample SATCOM Aero decoded output",
            SourceState.Offline,
            "application/x-ndjson; domain=satcom-aero",
            0,
            0,
            null),
        ["local-radiosonde-json"] = new ImportStateSnapshot(
            "local-radiosonde-json",
            "Local radiosonde telemetry sample",
            SourceState.Offline,
            "application/x-ndjson; domain=radiosonde",
            0,
            0,
            null)
    };
    private long _sequence;
    private int _diagnosticSequence;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public event EventHandler<ImportDiagnostic>? DiagnosticReceived;

    public event EventHandler<ImportStateSnapshot>? ImportStateChanged;

    public IReadOnlyList<ImportStateSnapshot> GetImports()
    {
        lock (_gate)
        {
            return _imports.Values.OrderBy(import => import.Id).ToArray();
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

    public async Task<ImportReplayResult> StartImportAsync(string importId, CancellationToken cancellationToken = default)
    {
        if (string.Equals(importId, "local-adsb-readsb-json", StringComparison.OrdinalIgnoreCase))
        {
            return await StartAdsbImportAsync(importId, cancellationToken);
        }

        if (string.Equals(importId, "local-remote-id-json", StringComparison.OrdinalIgnoreCase))
        {
            return await StartRemoteIdImportAsync(importId, cancellationToken);
        }

        if (string.Equals(importId, "sample-dumphfdl-json", StringComparison.OrdinalIgnoreCase))
        {
            return await StartExternalDecoderImportAsync(importId, "hfdl", "sample-dumphfdl.ndjson", "application/x-ndjson; domain=dumphfdl", "dumphfdl", cancellationToken);
        }

        if (string.Equals(importId, "sample-dumpvdl2-json", StringComparison.OrdinalIgnoreCase))
        {
            return await StartExternalDecoderImportAsync(importId, "vdl2", "sample-dumpvdl2.ndjson", "application/x-ndjson; domain=dumpvdl2", "dumpvdl2", cancellationToken);
        }

        if (string.Equals(importId, "sample-satcom-json", StringComparison.OrdinalIgnoreCase))
        {
            return await StartExternalDecoderImportAsync(importId, "satcom", "sample-satcom.ndjson", "application/x-ndjson; domain=satcom-aero", "satcom-import", cancellationToken);
        }

        if (string.Equals(importId, "local-radiosonde-json", StringComparison.OrdinalIgnoreCase))
        {
            return await StartRadioSondeImportAsync(importId, cancellationToken);
        }

        if (!string.Equals(importId, "local-acars-ndjson", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown import source '{importId}'.");
        }

        SetState(importId, SourceState.Starting, 0, 0, null);

        var importedAtUtc = clock.UtcNow;
        var accepted = 0;
        var rejected = 0;
        var fixturePath = FindFixturePath("acars", "local-acars.ndjson");
        var lineNumber = 0;
        var seenPayloads = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset? previousOriginalTimestampUtc = null;

        foreach (var line in await File.ReadAllLinesAsync(fixturePath, cancellationToken))
        {
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var rawReference = $"fixtures/imports/acars/local-acars.ndjson:{lineNumber}";

            if (line.Length > MaximumRecordBytes)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_RECORD_OVERSIZED", "Decoded-data import record exceeded the maximum size and was quarantined.", rawReference);
                continue;
            }

            DecodedImportRecord? record;

            try
            {
                record = JsonSerializer.Deserialize<DecodedImportRecord>(line, JsonOptions);
            }
            catch (JsonException exception)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_JSON_INVALID", $"Decoded-data import record is not valid JSON: {exception.Message}", rawReference);
                continue;
            }

            if (record?.RawPayload is not { Length: > 0 })
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_SCHEMA_MISMATCH", "Decoded-data import record is missing rawPayload and was quarantined.", rawReference);
                continue;
            }

            if (!seenPayloads.Add(record.RawPayload))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_DUPLICATE_RECORD", "Decoded-data import record duplicated an earlier raw payload and was quarantined.", rawReference);
                continue;
            }

            if (record.OriginalTimestampUtc is not null && previousOriginalTimestampUtc is not null && record.OriginalTimestampUtc < previousOriginalTimestampUtc)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_OUT_OF_ORDER", "Decoded-data import record timestamp moved backward and was quarantined.", rawReference);
                continue;
            }

            previousOriginalTimestampUtc = record.OriginalTimestampUtc ?? previousOriginalTimestampUtc;

            if (record.Kind is not null && record.Kind != AviationMessageKind.Acars)
            {
                rejected++;
                AddDiagnostic(importId, "Info", "IMPORT_UNSUPPORTED_KIND", $"Decoded-data import kind '{record.Kind}' is not supported by the starter importer.", rawReference);
                continue;
            }

            var sequence = Interlocked.Increment(ref _sequence);
            var receivedAtUtc = clock.UtcNow;
            var message = acarsMessageParser.Parse(new AcarsParserInput(
                record.RawPayload,
                sequence,
                receivedAtUtc,
                new IngestionProvenance(
                    IngestionPath.ImportedDecodedData,
                    importId,
                    "Local ACARS NDJSON sample",
                    record.SourceApp,
                    record.SourceFormat ?? "application/x-ndjson; domain=acars",
                    "FileNdjsonImportManager",
                    "0.1.0",
                    record.OriginalTimestampUtc,
                    receivedAtUtc,
                    rawReference),
                record.FrequencyMHz,
                record.Transport ?? "Imported ACARS"));

            await messageBus.PublishAsync(message, cancellationToken);
            accepted++;
            AddDiagnostic(importId, "Info", "IMPORT_RECORD_ACCEPTED", $"Imported ACARS record {rawReference}.", rawReference);
        }

        SetState(importId, SourceState.Online, accepted, rejected, rejected == 0 ? null : $"{rejected} record(s) quarantined");

        return new ImportReplayResult(importId, "application/x-ndjson; domain=acars", accepted, rejected, importedAtUtc);
    }

    private async Task<ImportReplayResult> StartAdsbImportAsync(string importId, CancellationToken cancellationToken)
    {
        SetState(importId, SourceState.Starting, 0, 0, null);

        var importedAtUtc = clock.UtcNow;
        var accepted = 0;
        var rejected = 0;
        var fixturePath = FindFixturePath("adsb", "local-readsb-aircraft.ndjson");
        var seenAircraft = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;

        foreach (var line in await File.ReadAllLinesAsync(fixturePath, cancellationToken))
        {
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var rawReference = $"fixtures/imports/adsb/local-readsb-aircraft.ndjson:{lineNumber}";

            if (line.Length > MaximumRecordBytes)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_RECORD_OVERSIZED", "ADS-B import record exceeded the maximum size and was quarantined.", rawReference);
                continue;
            }

            AdsbAircraftImportRecord? record;

            try
            {
                record = JsonSerializer.Deserialize<AdsbAircraftImportRecord>(line, JsonOptions);
            }
            catch (JsonException exception)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_JSON_INVALID", $"ADS-B import record is not valid JSON: {exception.Message}", rawReference);
                continue;
            }

            if (record?.Hex is not { Length: 6 } hex)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_SCHEMA_MISMATCH", "ADS-B import record is missing a six-character hex aircraft address and was quarantined.", rawReference);
                continue;
            }

            hex = hex.ToUpperInvariant();

            if (!seenAircraft.Add(hex))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_DUPLICATE_RECORD", $"ADS-B aircraft {hex} duplicated an earlier record in the same import run and was quarantined.", rawReference);
                continue;
            }

            if (record.SeenPos is > 900)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_ADSB_STALE", $"ADS-B aircraft {hex} position is stale and was quarantined.", rawReference);
                continue;
            }

            if (!IsValidCoordinate(record.Lat, record.Lon))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_ADSB_INVALID_POSITION", $"ADS-B aircraft {hex} has invalid coordinates and was quarantined.", rawReference);
                continue;
            }

            var receivedAtUtc = clock.UtcNow;
            var updatedAtUtc = receivedAtUtc - TimeSpan.FromSeconds(record.SeenPos ?? 0);
            aircraftRegistryLookup.TryLookup(hex, out var registryEntry);
            var track = new AircraftTrackSnapshot(
                hex,
                updatedAtUtc,
                record.Lat,
                record.Lon,
                record.AltBaro,
                importId,
                ParserConfidence.Observed,
                string.IsNullOrWhiteSpace(record.Flight) ? null : record.Flight.Trim(),
                record.Gs,
                record.Track,
                "Imported ADS-B/readsb",
                $"adsb-{hex}",
                false,
                new IngestionProvenance(
                    IngestionPath.ImportedDecodedData,
                    importId,
                    "Local ADS-B readsb sample",
                    record.SourceApp ?? "readsb",
                    record.SourceFormat ?? "application/x-ndjson; domain=adsb-readsb",
                    "FileNdjsonImportManager",
                    "0.2.0",
                    record.OriginalTimestampUtc,
                    receivedAtUtc,
                    rawReference),
                EmitterCategory: string.IsNullOrWhiteSpace(record.Category) ? null : record.Category,
                AircraftType: string.IsNullOrWhiteSpace(record.AircraftType) ? registryEntry.IcaoTypeCode : record.AircraftType,
                Registration: string.IsNullOrWhiteSpace(record.Registration) ? registryEntry.Registration : record.Registration.Trim(),
                OperatorName: registryEntry.Operator,
                IsMilitary: registryEntry.IsMilitary);

            if (aircraftTrackStore.Upsert(track))
            {
                accepted++;
                AddDiagnostic(importId, "Info", "IMPORT_RECORD_ACCEPTED", $"Imported ADS-B aircraft {hex}.", rawReference);
            }
            else
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_OUT_OF_ORDER", $"ADS-B aircraft {hex} update was older than the current track and was quarantined.", rawReference);
            }
        }

        SetState(importId, SourceState.Online, accepted, rejected, rejected == 0 ? null : $"{rejected} record(s) quarantined");
        return new ImportReplayResult(importId, "application/x-ndjson; domain=adsb-readsb", accepted, rejected, importedAtUtc);
    }

    private async Task<ImportReplayResult> StartRemoteIdImportAsync(string importId, CancellationToken cancellationToken)
    {
        SetState(importId, SourceState.Starting, 0, 0, null);

        var importedAtUtc = clock.UtcNow;
        var accepted = 0;
        var rejected = 0;
        var fixturePath = FindFixturePath("remote-id", "commercial-drone-sample.ndjson");
        var seenObservationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;

        foreach (var line in await File.ReadAllLinesAsync(fixturePath, cancellationToken))
        {
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var rawReference = $"fixtures/imports/remote-id/commercial-drone-sample.ndjson:{lineNumber}";

            if (line.Length > MaximumRecordBytes)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_RECORD_OVERSIZED", "Remote ID record exceeded the maximum size and was quarantined.", rawReference);
                continue;
            }

            RemoteIdDroneImportRecord? record;

            try
            {
                record = JsonSerializer.Deserialize<RemoteIdDroneImportRecord>(line, JsonOptions);
            }
            catch (JsonException exception)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_JSON_INVALID", $"Remote ID record is not valid JSON: {exception.Message}", rawReference);
                continue;
            }

            var uasId = string.IsNullOrWhiteSpace(record?.UasId) ? record?.SerialNumber : record.UasId;

            if (record is null || string.IsNullOrWhiteSpace(uasId))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_SCHEMA_MISMATCH", "Remote ID record is missing a UAS ID and was quarantined.", rawReference);
                continue;
            }

            uasId = uasId.Trim();
            var messageType = string.IsNullOrWhiteSpace(record.MessageType) ? "Unknown" : record.MessageType.Trim();
            var observationId = string.IsNullOrWhiteSpace(record.ObservationId) ? null : record.ObservationId.Trim();

            if (observationId is not null && !seenObservationIds.Add(observationId))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_DUPLICATE_RECORD", $"Remote ID observation {observationId} for {uasId} duplicated an earlier record in the same import run and was quarantined.", rawReference);
                continue;
            }

            var hasPosition = record.Latitude.HasValue || record.Longitude.HasValue;

            if (hasPosition && record.SeenSeconds is > 120)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_REMOTE_ID_STALE", $"Remote ID observation for {uasId} is stale and was quarantined.", rawReference);
                continue;
            }

            if (hasPosition && !IsValidCoordinate(record.Latitude, record.Longitude))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_REMOTE_ID_INVALID_POSITION", $"Remote ID observation for {uasId} has invalid coordinates and was quarantined.", rawReference);
                continue;
            }

            var receivedAtUtc = clock.UtcNow;
            var updatedAtUtc = record.OriginalTimestampUtc ?? receivedAtUtc - TimeSpan.FromSeconds(record.SeenSeconds ?? 0);
            var operationType = string.IsNullOrWhiteSpace(record.OperationType) ? null : record.OperationType.Trim();
            var serialNumber = string.IsNullOrWhiteSpace(record.SerialNumber) ? null : record.SerialNumber.Trim();
            var provenance = new IngestionProvenance(
                IngestionPath.ImportedDecodedData,
                importId,
                "Local Remote ID commercial drone sample",
                record.SourceApp ?? "remote-id-receiver",
                record.SourceFormat ?? "application/x-ndjson; domain=remote-id",
                "FileNdjsonImportManager",
                "0.2.0",
                record.OriginalTimestampUtc,
                receivedAtUtc,
                rawReference);

            remoteIdObservationStore?.Add(new RemoteIdObservation(
                $"{importId}:{observationId ?? lineNumber.ToString()}",
                uasId,
                record.UasIdType,
                record.UaType,
                record.OperatorId,
                record.OperatorIdType,
                operationType,
                record.OperationTypeSource,
                messageType,
                record.OriginalTimestampUtc,
                receivedAtUtc,
                record.Latitude,
                record.Longitude,
                record.AltitudeBarometricMeters,
                record.AltitudeGeodeticMeters,
                record.HeightAboveGroundMeters,
                record.AltitudeReference,
                record.SpeedMetersPerSecond,
                record.DirectionDegrees,
                record.VerticalSpeedMetersPerSecond,
                record.HorizontalAccuracyMeters,
                record.VerticalAccuracyMeters,
                record.SpeedAccuracyMetersPerSecond,
                record.DirectionAccuracyDegrees,
                record.OperatorLatitude,
                record.OperatorLongitude,
                record.AreaCount,
                record.AreaRadiusMeters,
                record.AreaCeilingMeters,
                record.AreaFloorMeters,
                record.SelfIdText,
                record.AuthenticationStatus,
                record.AuthenticationVerifiedBySource,
                record.Radio,
                record.Rssi,
                record.Channel,
                record.ReceiverId,
                record.SourceMac,
                record.ValidationStatus,
                record.ValidationWarnings ?? [],
                record.RawPayload,
                rawReference,
                provenance));

            accepted++;

            if (!hasPosition)
            {
                AddDiagnostic(importId, "Info", "IMPORT_RECORD_ACCEPTED", $"Retained Remote ID {messageType} observation for {uasId}; no position was supplied.", rawReference);
                continue;
            }

            var track = new AircraftTrackSnapshot(
                $"RID:{uasId}",
                updatedAtUtc,
                record.Latitude,
                record.Longitude,
                null,
                importId,
                ParserConfidence.Observed,
                null,
                record.SpeedMetersPerSecond * 1.943844,
                record.DirectionDegrees,
                "Remote ID drone",
                $"rid-{uasId}",
                false,
                provenance,
                AircraftType: "Remote ID UAS",
                OperatorName: null,
                RemoteIdSerialNumber: serialNumber,
                RemoteIdOperatorId: string.IsNullOrWhiteSpace(record.OperatorId) ? null : record.OperatorId.Trim(),
                RemoteIdOperationType: operationType,
                RemoteIdUasIdType: record.UasIdType,
                RemoteIdUaType: record.UaType,
                RemoteIdOperatorIdType: record.OperatorIdType,
                RemoteIdOperationTypeSource: record.OperationTypeSource,
                RemoteIdMessageType: messageType,
                RemoteIdAltitudeGeodeticMeters: record.AltitudeGeodeticMeters,
                RemoteIdHeightAboveGroundMeters: record.HeightAboveGroundMeters,
                RemoteIdAltitudeBarometricMeters: record.AltitudeBarometricMeters,
                RemoteIdAltitudeReference: record.AltitudeReference,
                RemoteIdVerticalSpeedMetersPerSecond: record.VerticalSpeedMetersPerSecond,
                RemoteIdHorizontalAccuracyMeters: record.HorizontalAccuracyMeters,
                RemoteIdVerticalAccuracyMeters: record.VerticalAccuracyMeters,
                RemoteIdSpeedAccuracyMetersPerSecond: record.SpeedAccuracyMetersPerSecond,
                RemoteIdDirectionAccuracyDegrees: record.DirectionAccuracyDegrees,
                RemoteIdBroadcastAtUtc: record.OriginalTimestampUtc,
                RemoteIdRadio: record.Radio,
                RemoteIdSourceMac: record.SourceMac,
                RemoteIdChannel: record.Channel,
                RemoteIdRssi: record.Rssi,
                RemoteIdReceiverId: record.ReceiverId);

            if (!aircraftTrackStore.Upsert(track))
            {
                AddDiagnostic(importId, "Warning", "IMPORT_OUT_OF_ORDER", $"Remote ID observation for {uasId} was retained, but its position did not replace the newer current track.", rawReference);
                continue;
            }

            AddDiagnostic(importId, "Info", "IMPORT_RECORD_ACCEPTED", $"Imported Remote ID {messageType} observation for {uasId}.", rawReference);
        }

        SetState(importId, SourceState.Online, accepted, rejected, rejected == 0 ? null : $"{rejected} record(s) quarantined");
        return new ImportReplayResult(importId, "application/x-ndjson; domain=remote-id", accepted, rejected, importedAtUtc);
    }

    private async Task<ImportReplayResult> StartRadioSondeImportAsync(string importId, CancellationToken cancellationToken)
    {
        if (sondeTrackStore is null)
        {
            throw new InvalidOperationException("Radiosonde import requires a sonde track store.");
        }

        SetState(importId, SourceState.Starting, 0, 0, null);

        var importedAtUtc = clock.UtcNow;
        var accepted = 0;
        var rejected = 0;
        var fixturePath = FindFixturePath("radiosonde", "local-radiosonde-telemetry.ndjson");
        var seenFrames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;

        foreach (var line in await File.ReadAllLinesAsync(fixturePath, cancellationToken))
        {
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var rawReference = $"fixtures/imports/radiosonde/local-radiosonde-telemetry.ndjson:{lineNumber}";

            if (line.Length > MaximumRecordBytes)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_RECORD_OVERSIZED", "Radiosonde import record exceeded the maximum size and was quarantined.", rawReference);
                continue;
            }

            RadioSondeImportRecord? record;

            try
            {
                record = JsonSerializer.Deserialize<RadioSondeImportRecord>(line, JsonOptions);
            }
            catch (JsonException exception)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_JSON_INVALID", $"Radiosonde import record is not valid JSON: {exception.Message}", rawReference);
                continue;
            }

            if (string.IsNullOrWhiteSpace(record?.Serial))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_SCHEMA_MISMATCH", "Radiosonde import record is missing a serial number and was quarantined.", rawReference);
                continue;
            }

            var serial = record.Serial.Trim();
            var frameKey = $"{serial}:{record.FrameSequence}";

            if (!seenFrames.Add(frameKey))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_DUPLICATE_RECORD", $"Radiosonde {serial} frame {record.FrameSequence} duplicated an earlier record in the same import run and was quarantined.", rawReference);
                continue;
            }

            if (!IsValidCoordinate(record.Lat, record.Lon))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_RADIOSONDE_INVALID_POSITION", $"Radiosonde {serial} has invalid coordinates and was quarantined.", rawReference);
                continue;
            }

            var receivedAtUtc = clock.UtcNow;
            var updatedAtUtc = record.OriginalTimestampUtc ?? receivedAtUtc;
            var track = new SondeTelemetrySnapshot(
                serial,
                updatedAtUtc,
                record.Lat,
                record.Lon,
                record.AltitudeMeters,
                importId,
                ParserConfidence.Observed,
                string.IsNullOrWhiteSpace(record.SondeType) ? null : record.SondeType.Trim(),
                record.AscentRateMetersPerSecond,
                record.TemperatureCelsius,
                record.HumidityPercent,
                record.PressureHpa,
                "Imported radiosonde telemetry",
                $"radiosonde-{serial}",
                false,
                new IngestionProvenance(
                    IngestionPath.ImportedDecodedData,
                    importId,
                    "Local radiosonde telemetry sample",
                    record.SourceApp ?? "rs41mod",
                    record.SourceFormat ?? "application/x-ndjson; domain=radiosonde",
                    "FileNdjsonImportManager",
                    "0.2.0",
                    record.OriginalTimestampUtc,
                    receivedAtUtc,
                    rawReference),
                FrameSequence: record.FrameSequence,
                CrcValid: record.CrcValid ?? true,
                BurstKill: record.BurstKill ?? false,
                BatteryVoltage: record.BatteryVoltage,
                FrequencyMHz: record.FrequencyMHz,
                Rssi: record.Rssi);

            if (!track.CrcValid)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_RADIOSONDE_CRC_INVALID", $"Radiosonde {serial} frame failed CRC and was quarantined.", rawReference);
                continue;
            }

            if (sondeTrackStore.Upsert(track))
            {
                accepted++;
                AddDiagnostic(importId, "Info", "IMPORT_RECORD_ACCEPTED", $"Imported radiosonde {serial}.", rawReference);
            }
            else
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_OUT_OF_ORDER", $"Radiosonde {serial} update was older than the current track and was quarantined.", rawReference);
            }
        }

        SetState(importId, SourceState.Online, accepted, rejected, rejected == 0 ? null : $"{rejected} record(s) quarantined");
        return new ImportReplayResult(importId, "application/x-ndjson; domain=radiosonde", accepted, rejected, importedAtUtc);
    }

    private async Task<ImportReplayResult> StartExternalDecoderImportAsync(
        string importId,
        string domain,
        string fileName,
        string sourceFormat,
        string sourceApp,
        CancellationToken cancellationToken)
    {
        SetState(importId, SourceState.Starting, 0, 0, null);

        var importedAtUtc = clock.UtcNow;
        var accepted = 0;
        var rejected = 0;
        var fixturePath = FindFixturePath(domain, fileName);
        var seenPayloads = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;

        foreach (var line in await File.ReadAllLinesAsync(fixturePath, cancellationToken))
        {
            lineNumber++;
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var rawReference = $"fixtures/imports/{domain}/{fileName}:{lineNumber}";

            if (line.Length > MaximumRecordBytes)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_RECORD_OVERSIZED", "External decoder record exceeded the maximum size and was quarantined.", rawReference);
                continue;
            }

            ExternalDecoderRecord? record;

            try
            {
                record = JsonSerializer.Deserialize<ExternalDecoderRecord>(line, JsonOptions);
            }
            catch (JsonException exception)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_JSON_INVALID", $"External decoder record is not valid JSON: {exception.Message}", rawReference);
                continue;
            }

            if (record?.Payload is not { Length: > 0 } payload)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_SCHEMA_MISMATCH", "External decoder record is missing payload and was quarantined.", rawReference);
                continue;
            }

            if (record.IsValid == false)
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_LINK_VALIDATION_FAILED", "External decoder record reported failed link validation and was quarantined.", rawReference);
                continue;
            }

            if (!seenPayloads.Add(payload))
            {
                rejected++;
                AddDiagnostic(importId, "Warning", "IMPORT_DUPLICATE_RECORD", "External decoder record duplicated an earlier payload and was quarantined.", rawReference);
                continue;
            }

            var sequence = Interlocked.Increment(ref _sequence);
            var receivedAtUtc = clock.UtcNow;
            var provenance = new IngestionProvenance(
                IngestionPath.ExternalProcess,
                importId,
                _imports[importId].Name,
                record.SourceApp ?? sourceApp,
                record.SourceFormat ?? sourceFormat,
                "FileNdjsonImportManager",
                "0.3.0",
                record.OriginalTimestampUtc,
                receivedAtUtc,
                rawReference);

            var kind = record.Kind ?? AviationMessageKind.Acars;
            var message = kind == AviationMessageKind.Acars
                ? acarsMessageParser.Parse(new AcarsParserInput(payload, sequence, receivedAtUtc, provenance, record.FrequencyMHz, record.Transport ?? record.Transport ?? domain.ToUpperInvariant()))
                : CreateExternalPlaceholderMessage(sequence, receivedAtUtc, kind, payload, record, provenance, domain);

            if (kind == AviationMessageKind.AdsC)
            {
                UpsertAdsCTrack(record, provenance, receivedAtUtc, payload, domain);
            }

            await messageBus.PublishAsync(message, cancellationToken);
            accepted++;
            AddDiagnostic(importId, "Info", "IMPORT_RECORD_ACCEPTED", $"Imported {kind} record from {sourceApp}.", rawReference);
        }

        SetState(importId, SourceState.Online, accepted, rejected, rejected == 0 ? null : $"{rejected} record(s) quarantined");
        return new ImportReplayResult(importId, sourceFormat, accepted, rejected, importedAtUtc);
    }

    private static NormalizedAviationMessage CreateExternalPlaceholderMessage(
        long sequence,
        DateTimeOffset receivedAtUtc,
        AviationMessageKind kind,
        string payload,
        ExternalDecoderRecord record,
        IngestionProvenance provenance,
        string domain)
    {
        var datalink = CreateDatalinkDetails(kind, payload, record, domain);
        var satcom = CreateSatcomMetadata(record, domain);

        return new NormalizedAviationMessage(
            $"{provenance.SourceId}-{kind.ToString().ToLowerInvariant()}-{sequence}",
            sequence,
            receivedAtUtc,
            kind,
            BuildExternalSummary(record, domain, kind, datalink),
            null,
            null,
            record.Transport ?? domain.ToUpperInvariant(),
            record.FrequencyMHz,
            ParserConfidence.Observed,
            provenance,
            payload,
            [new AviationWarning("EXTERNAL_PAYLOAD_PLACEHOLDER", "Payload preserved for a future structured application parser.", "Info")],
                datalink,
                satcom);
    }

        private static string BuildExternalSummary(ExternalDecoderRecord record, string domain, AviationMessageKind kind, DatalinkMessageDetails datalink)
        {
            var reference = datalink.MessageReference is null ? string.Empty : $" {datalink.MessageReference}";
            return $"{record.Transport ?? domain.ToUpperInvariant()} {kind}{reference} {datalink.Category} from {record.SourceApp ?? "external decoder"}";
        }

    private static DatalinkMessageDetails CreateDatalinkDetails(AviationMessageKind kind, string payload, ExternalDecoderRecord record, string domain)
    {
        return new DatalinkMessageDetails(
            kind.ToString(),
            record.Direction ?? "unknown",
            record.MessageReference ?? ExtractMessageReference(kind, payload),
            record.AcknowledgementState,
            ClassifyDatalinkCategory(kind, payload, domain),
            record.AircraftIdentifier ?? ExtractAircraftIdentifier(payload),
            record.Latitude,
            record.Longitude,
            record.AltitudeFeet,
            payload);
    }

    private static SatcomTransportMetadata? CreateSatcomMetadata(ExternalDecoderRecord record, string domain)
    {
        if (!string.Equals(record.Transport, "SATCOM", StringComparison.OrdinalIgnoreCase) && !string.Equals(domain, "satcom", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new SatcomTransportMetadata(
            record.Satellite,
            record.Channel,
            record.Bearer,
            record.GroundEndpoint,
            record.ReassemblyState ?? "unknown");
    }

    private static string? ExtractMessageReference(AviationMessageKind kind, string payload)
    {
        var tokens = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (kind == AviationMessageKind.Cpdlc)
        {
            return tokens.FirstOrDefault(token => token.StartsWith("UM", StringComparison.OrdinalIgnoreCase) || token.StartsWith("DM", StringComparison.OrdinalIgnoreCase));
        }

        if (kind == AviationMessageKind.AdsC)
        {
            return tokens.Contains("PERIODIC", StringComparer.OrdinalIgnoreCase) ? "ADS-C-PERIODIC" : null;
        }

        return null;
    }

    private static string ClassifyDatalinkCategory(AviationMessageKind kind, string payload, string domain)
    {
        return kind switch
        {
            AviationMessageKind.Cpdlc when payload.Contains("MAINTAIN", StringComparison.OrdinalIgnoreCase) => "CPDLC clearance/instruction",
            AviationMessageKind.Cpdlc => "CPDLC preserved payload",
            AviationMessageKind.AdsC when payload.Contains("PERIODIC", StringComparison.OrdinalIgnoreCase) => "ADS-C periodic report",
            AviationMessageKind.AdsC => "ADS-C preserved report",
            AviationMessageKind.Satcom => "Unknown/proprietary SATCOM",
            _ => $"{domain.ToUpperInvariant()} preserved payload"
        };
    }

    private void UpsertAdsCTrack(
        ExternalDecoderRecord record,
        IngestionProvenance provenance,
        DateTimeOffset receivedAtUtc,
        string payload,
        string domain)
    {
        var aircraftIdentifier = record.AircraftIdentifier ?? ExtractAircraftIdentifier(payload);

        if (aircraftIdentifier is null || !IsValidCoordinate(record.Latitude, record.Longitude))
        {
            return;
        }

        aircraftTrackStore.Upsert(new AircraftTrackSnapshot(
            aircraftIdentifier,
            receivedAtUtc,
            record.Latitude,
            record.Longitude,
            record.AltitudeFeet,
            provenance.SourceId,
            ParserConfidence.Observed,
            aircraftIdentifier,
            null,
            null,
            $"ADS-C over {record.Transport ?? domain.ToUpperInvariant()}",
            $"adsc-{aircraftIdentifier}",
            false,
            provenance));
    }

    private static string? ExtractAircraftIdentifier(string payload)
    {
        return payload.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(token => token.StartsWith('N') && token.Length is >= 3 and <= 6);
    }

    private static bool IsValidCoordinate(double? latitude, double? longitude)
    {
        return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    private static string FindFixturePath(string domain, string fileName)
    {
        var directory = AppContext.BaseDirectory;

        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "fixtures", "imports", domain, fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException($"Could not find fixtures/imports/{domain}/{fileName}.");
    }

    private void SetState(string importId, SourceState state, long accepted, long rejected, string? lastError)
    {
        ImportStateSnapshot snapshot;

        lock (_gate)
        {
            var current = _imports[importId];
            snapshot = current with
            {
                State = state,
                AcceptedRecords = accepted,
                RejectedRecords = rejected,
                LastError = lastError
            };
            _imports[importId] = snapshot;
        }

        ImportStateChanged?.Invoke(this, snapshot);
    }

    private void AddDiagnostic(string importId, string severity, string code, string message, string? rawReference)
    {
        var diagnostic = new ImportDiagnostic(
            $"import-diagnostic-{Interlocked.Increment(ref _diagnosticSequence)}",
            clock.UtcNow,
            importId,
            severity,
            code,
            message,
            rawReference);

        lock (_gate)
        {
            _diagnostics.Add(diagnostic);
            recordStore?.SaveImportDiagnostic(diagnostic);

            if (_diagnostics.Count > MaximumDiagnostics)
            {
                _diagnostics.RemoveRange(0, _diagnostics.Count - MaximumDiagnostics);
            }
        }

        DiagnosticReceived?.Invoke(this, diagnostic);
    }
}