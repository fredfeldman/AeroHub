using System.Text.Json;
using System.Text.Json.Serialization;
using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class JsonlRecordStore : IRecordStore
{
    private const int MaximumPositionSamplesPerAircraft = 500;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _rootPath;
    private readonly string _messagePath;
    private readonly string _trackPath;
    private readonly string _diagnosticPath;
    private readonly string _positionPath;
    private readonly string _sondeTrackPath;
    private readonly string _sondePositionPath;
    private readonly List<NormalizedAviationMessage> _messages = [];
    private readonly Dictionary<string, AircraftTrackSnapshot> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ImportDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, List<AircraftPositionSample>> _positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SondeTelemetrySnapshot> _sondeTracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SondePositionSample>> _sondePositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _messageIds = new(StringComparer.OrdinalIgnoreCase);

    public JsonlRecordStore()
        : this(Path.Combine(AppContext.BaseDirectory, "data", "store"))
    {
    }

    public JsonlRecordStore(string rootPath)
    {
        _rootPath = rootPath;
        _messagePath = Path.Combine(_rootPath, "messages.jsonl");
        _trackPath = Path.Combine(_rootPath, "aircraft-tracks.jsonl");
        _diagnosticPath = Path.Combine(_rootPath, "import-diagnostics.jsonl");
        _positionPath = Path.Combine(_rootPath, "aircraft-positions.jsonl");
        _sondeTrackPath = Path.Combine(_rootPath, "sonde-tracks.jsonl");
        _sondePositionPath = Path.Combine(_rootPath, "sonde-positions.jsonl");
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        Directory.CreateDirectory(_rootPath);
        LoadExistingRecords();
    }

    public void SaveMessage(NormalizedAviationMessage message)
    {
        lock (_gate)
        {
            if (!_messageIds.Add(message.Id))
            {
                return;
            }

            _messages.Add(message);
            AppendRecord(_messagePath, message);
        }
    }

    public IReadOnlyList<NormalizedAviationMessage> GetRecentMessages(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, 1000);

        lock (_gate)
        {
            return _messages
                .OrderByDescending(message => message.ReceivedAtUtc)
                .ThenByDescending(message => message.Sequence)
                .Take(boundedLimit)
                .ToArray();
        }
    }

    public bool SaveTrack(AircraftTrackSnapshot track)
    {
        lock (_gate)
        {
            if (_tracks.TryGetValue(track.AircraftIdentifier, out var existing) && existing.UpdatedAtUtc >= track.UpdatedAtUtc)
            {
                return false;
            }

            _tracks[track.AircraftIdentifier] = track;
            RewriteRecords(_trackPath, _tracks.Values.OrderBy(item => item.AircraftIdentifier));
            return true;
        }
    }

    public IReadOnlyList<AircraftTrackSnapshot> GetCurrentTracks(DateTimeOffset utcNow, TimeSpan staleAfter)
    {
        lock (_gate)
        {
            return _tracks.Values
                .Select(track => track with { IsStale = utcNow - track.UpdatedAtUtc > staleAfter })
                .Where(track => !track.IsStale)
                .OrderBy(track => track.AircraftIdentifier)
                .ToArray();
        }
    }

    public void SaveImportDiagnostic(ImportDiagnostic diagnostic)
    {
        lock (_gate)
        {
            _diagnostics.Add(diagnostic);
            AppendRecord(_diagnosticPath, diagnostic);
        }
    }

    public IReadOnlyList<ImportDiagnostic> GetImportDiagnostics(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, 1000);

        lock (_gate)
        {
            return _diagnostics
                .OrderByDescending(diagnostic => diagnostic.OccurredAtUtc)
                .Take(boundedLimit)
                .ToArray();
        }
    }

    public void SavePosition(AircraftPositionSample sample)
    {
        lock (_gate)
        {
            if (!_positions.TryGetValue(sample.AircraftIdentifier, out var samples))
            {
                samples = [];
                _positions[sample.AircraftIdentifier] = samples;
            }

            samples.Add(sample);

            if (samples.Count > MaximumPositionSamplesPerAircraft)
            {
                samples.RemoveAt(0);
            }

            AppendRecord(_positionPath, sample);
        }
    }

    public IReadOnlyList<AircraftPositionSample> GetPositionHistory(string aircraftIdentifier, int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumPositionSamplesPerAircraft);

        lock (_gate)
        {
            if (!_positions.TryGetValue(aircraftIdentifier, out var samples))
            {
                return [];
            }

            return samples
                .OrderBy(sample => sample.RecordedAtUtc)
                .TakeLast(boundedLimit)
                .ToArray();
        }
    }

    public bool SaveSondeTrack(SondeTelemetrySnapshot track)
    {
        lock (_gate)
        {
            if (_sondeTracks.TryGetValue(track.Serial, out var existing) && existing.UpdatedAtUtc >= track.UpdatedAtUtc)
            {
                return false;
            }

            _sondeTracks[track.Serial] = track;
            RewriteRecords(_sondeTrackPath, _sondeTracks.Values.OrderBy(item => item.Serial));
            return true;
        }
    }

    public IReadOnlyList<SondeTelemetrySnapshot> GetCurrentSondeTracks(DateTimeOffset utcNow, TimeSpan staleAfter)
    {
        lock (_gate)
        {
            return _sondeTracks.Values
                .Select(track => track with { IsStale = utcNow - track.UpdatedAtUtc > staleAfter })
                .Where(track => !track.IsStale)
                .OrderBy(track => track.Serial)
                .ToArray();
        }
    }

    public void SaveSondePosition(SondePositionSample sample)
    {
        lock (_gate)
        {
            if (!_sondePositions.TryGetValue(sample.Serial, out var samples))
            {
                samples = [];
                _sondePositions[sample.Serial] = samples;
            }

            samples.Add(sample);

            if (samples.Count > MaximumPositionSamplesPerAircraft)
            {
                samples.RemoveAt(0);
            }

            AppendRecord(_sondePositionPath, sample);
        }
    }

    public IReadOnlyList<SondePositionSample> GetSondePositionHistory(string serial, int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumPositionSamplesPerAircraft);

        lock (_gate)
        {
            if (!_sondePositions.TryGetValue(serial, out var samples))
            {
                return [];
            }

            return samples
                .OrderBy(sample => sample.RecordedAtUtc)
                .TakeLast(boundedLimit)
                .ToArray();
        }
    }

    public StorageSnapshot GetSnapshot(DateTimeOffset checkedAtUtc)
    {
        lock (_gate)
        {
            return new StorageSnapshot(
                _rootPath,
                _messages.Count,
                _tracks.Count,
                _diagnostics.Count,
                GetBytesUsed(),
                checkedAtUtc,
                _positions.Values.Sum(samples => samples.Count));
        }
    }

    public StorageRetentionResult ApplyRetention(StorageRetentionPolicy policy, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            var messageCutoff = utcNow.AddDays(-Math.Max(1, policy.MessageRetentionDays));
            var trackCutoff = utcNow.AddDays(-Math.Max(1, policy.TrackRetentionDays));
            var diagnosticCutoff = utcNow.AddDays(-Math.Max(1, policy.DiagnosticRetentionDays));
            var beforeMessages = _messages.Count;
            var beforeTracks = _tracks.Count;
            var beforeDiagnostics = _diagnostics.Count;

            _messages.RemoveAll(message => message.ReceivedAtUtc < messageCutoff);
            _messageIds.Clear();

            foreach (var message in _messages)
            {
                _messageIds.Add(message.Id);
            }

            foreach (var staleTrack in _tracks.Values.Where(track => track.UpdatedAtUtc < trackCutoff).Select(track => track.AircraftIdentifier).ToArray())
            {
                _tracks.Remove(staleTrack);
            }

            _diagnostics.RemoveAll(diagnostic => diagnostic.OccurredAtUtc < diagnosticCutoff);

            foreach (var aircraftIdentifier in _positions.Keys.ToArray())
            {
                var samples = _positions[aircraftIdentifier];
                samples.RemoveAll(sample => sample.RecordedAtUtc < trackCutoff);

                if (samples.Count == 0)
                {
                    _positions.Remove(aircraftIdentifier);
                }
            }

            foreach (var serial in _sondeTracks.Values.Where(track => track.UpdatedAtUtc < trackCutoff).Select(track => track.Serial).ToArray())
            {
                _sondeTracks.Remove(serial);
            }

            foreach (var serial in _sondePositions.Keys.ToArray())
            {
                var samples = _sondePositions[serial];
                samples.RemoveAll(sample => sample.RecordedAtUtc < trackCutoff);

                if (samples.Count == 0)
                {
                    _sondePositions.Remove(serial);
                }
            }

            RewriteRecords(_messagePath, _messages);
            RewriteRecords(_trackPath, _tracks.Values.OrderBy(track => track.AircraftIdentifier));
            RewriteRecords(_diagnosticPath, _diagnostics);
            RewriteRecords(_positionPath, _positions.Values.SelectMany(samples => samples));
            RewriteRecords(_sondeTrackPath, _sondeTracks.Values.OrderBy(track => track.Serial));
            RewriteRecords(_sondePositionPath, _sondePositions.Values.SelectMany(samples => samples));

            return new StorageRetentionResult(
                beforeMessages - _messages.Count,
                beforeTracks - _tracks.Count,
                beforeDiagnostics - _diagnostics.Count,
                utcNow);
        }
    }

    public StorageExportResult ExportSnapshot(string exportName, DateTimeOffset exportedAtUtc)
    {
        lock (_gate)
        {
            var safeName = string.Join("-", exportName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var exportPath = Path.Combine(_rootPath, "exports", string.IsNullOrWhiteSpace(safeName) ? "snapshot" : safeName);
            Directory.CreateDirectory(exportPath);
            RewriteRecords(Path.Combine(exportPath, "messages.jsonl"), _messages);
            RewriteRecords(Path.Combine(exportPath, "aircraft-tracks.jsonl"), _tracks.Values.OrderBy(track => track.AircraftIdentifier));
            RewriteRecords(Path.Combine(exportPath, "import-diagnostics.jsonl"), _diagnostics);
            RewriteRecords(Path.Combine(exportPath, "aircraft-positions.jsonl"), _positions.Values.SelectMany(samples => samples));
            RewriteRecords(Path.Combine(exportPath, "sonde-tracks.jsonl"), _sondeTracks.Values.OrderBy(track => track.Serial));
            RewriteRecords(Path.Combine(exportPath, "sonde-positions.jsonl"), _sondePositions.Values.SelectMany(samples => samples));

            return new StorageExportResult(exportPath, _messages.Count, _tracks.Count, _diagnostics.Count, exportedAtUtc);
        }
    }

    private void LoadExistingRecords()
    {
        LoadRecords(_messagePath, _messages, message => _messageIds.Add(message.Id));
        LoadRecords<AircraftTrackSnapshot>(_trackPath, null, track => _tracks[track.AircraftIdentifier] = track);
        LoadRecords(_diagnosticPath, _diagnostics);
        LoadRecords<AircraftPositionSample>(_positionPath, null, sample =>
        {
            if (!_positions.TryGetValue(sample.AircraftIdentifier, out var samples))
            {
                samples = [];
                _positions[sample.AircraftIdentifier] = samples;
            }

            samples.Add(sample);

            if (samples.Count > MaximumPositionSamplesPerAircraft)
            {
                samples.RemoveAt(0);
            }
        });
        LoadRecords<SondeTelemetrySnapshot>(_sondeTrackPath, null, track => _sondeTracks[track.Serial] = track);
        LoadRecords<SondePositionSample>(_sondePositionPath, null, sample =>
        {
            if (!_sondePositions.TryGetValue(sample.Serial, out var samples))
            {
                samples = [];
                _sondePositions[sample.Serial] = samples;
            }

            samples.Add(sample);

            if (samples.Count > MaximumPositionSamplesPerAircraft)
            {
                samples.RemoveAt(0);
            }
        });
    }

    private void LoadRecords<T>(string path, List<T>? target, Action<T>? onRecord = null)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var record = JsonSerializer.Deserialize<T>(line, _jsonOptions);

            if (record is null)
            {
                continue;
            }

            target?.Add(record);
            onRecord?.Invoke(record);
        }
    }

    private void AppendRecord<T>(string path, T record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, JsonSerializer.Serialize(record, _jsonOptions) + Environment.NewLine);
    }

    private void RewriteRecords<T>(string path, IEnumerable<T> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, records.Select(record => JsonSerializer.Serialize(record, _jsonOptions)));
    }

    private long GetBytesUsed()
    {
        return Directory.EnumerateFiles(_rootPath, "*.jsonl", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
    }
}