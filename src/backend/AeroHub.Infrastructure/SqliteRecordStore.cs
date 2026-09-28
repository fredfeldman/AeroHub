using System.Text.Json;
using System.Text.Json.Serialization;
using AeroHub.Contracts;
using AeroHub.Core;
using Microsoft.Data.Sqlite;

namespace AeroHub.Infrastructure;

/// <summary>
/// SQLite-backed persistence for messages, aircraft tracks, import diagnostics, and a
/// rolling per-aircraft position-sample cache used to reconstruct flight track trails.
/// </summary>
public sealed class SqliteRecordStore : IRecordStore, IDisposable
{
    private const int MaximumPositionSamplesPerAircraft = 500;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _databasePath;
    private readonly SqliteConnection _connection;

    public SqliteRecordStore()
        : this(Path.Combine(AppContext.BaseDirectory, "data", "store", "aerohub.db"))
    {
    }

    public SqliteRecordStore(string databasePath)
    {
        _databasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());

        _connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                sequence INTEGER NOT NULL,
                received_at_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_messages_received_at ON messages(received_at_utc);

            CREATE TABLE IF NOT EXISTS aircraft_tracks (
                aircraft_identifier TEXT PRIMARY KEY,
                updated_at_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS import_diagnostics (
                id TEXT PRIMARY KEY,
                occurred_at_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_diagnostics_occurred_at ON import_diagnostics(occurred_at_utc);

            CREATE TABLE IF NOT EXISTS aircraft_positions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                aircraft_identifier TEXT NOT NULL,
                recorded_at_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_positions_aircraft ON aircraft_positions(aircraft_identifier, recorded_at_utc);

            CREATE TABLE IF NOT EXISTS sonde_tracks (
                serial TEXT PRIMARY KEY,
                updated_at_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sonde_positions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                serial TEXT NOT NULL,
                recorded_at_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_sonde_positions_serial ON sonde_positions(serial, recorded_at_utc);
            """;
        command.ExecuteNonQuery();
    }

    public void SaveMessage(NormalizedAviationMessage message)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO messages (id, sequence, received_at_utc, payload)
                VALUES (@id, @sequence, @receivedAtUtc, @payload);
                """;
            command.Parameters.AddWithValue("@id", message.Id);
            command.Parameters.AddWithValue("@sequence", message.Sequence);
            command.Parameters.AddWithValue("@receivedAtUtc", message.ReceivedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(message, _jsonOptions));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<NormalizedAviationMessage> GetRecentMessages(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, 1000);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT payload FROM messages
                ORDER BY received_at_utc DESC, sequence DESC
                LIMIT @limit;
                """;
            command.Parameters.AddWithValue("@limit", boundedLimit);

            return ReadPayloads<NormalizedAviationMessage>(command);
        }
    }

    public bool SaveTrack(AircraftTrackSnapshot track)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO aircraft_tracks (aircraft_identifier, updated_at_utc, payload)
                VALUES (@id, @updatedAtUtc, @payload)
                ON CONFLICT(aircraft_identifier) DO UPDATE SET
                    updated_at_utc = excluded.updated_at_utc,
                    payload = excluded.payload
                WHERE aircraft_tracks.updated_at_utc < excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("@id", track.AircraftIdentifier);
            command.Parameters.AddWithValue("@updatedAtUtc", track.UpdatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(track, _jsonOptions));
            return command.ExecuteNonQuery() > 0;
        }
    }

    public IReadOnlyList<AircraftTrackSnapshot> GetCurrentTracks(DateTimeOffset utcNow, TimeSpan staleAfter)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT payload FROM aircraft_tracks
                WHERE updated_at_utc >= @cutoff
                ORDER BY aircraft_identifier;
                """;
            command.Parameters.AddWithValue("@cutoff", (utcNow - staleAfter).ToString("O"));

            return ReadPayloads<AircraftTrackSnapshot>(command);
        }
    }

    public void SaveImportDiagnostic(ImportDiagnostic diagnostic)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO import_diagnostics (id, occurred_at_utc, payload)
                VALUES (@id, @occurredAtUtc, @payload);
                """;
            command.Parameters.AddWithValue("@id", diagnostic.Id);
            command.Parameters.AddWithValue("@occurredAtUtc", diagnostic.OccurredAtUtc.ToString("O"));
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(diagnostic, _jsonOptions));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ImportDiagnostic> GetImportDiagnostics(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, 1000);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT payload FROM import_diagnostics
                ORDER BY occurred_at_utc DESC
                LIMIT @limit;
                """;
            command.Parameters.AddWithValue("@limit", boundedLimit);

            return ReadPayloads<ImportDiagnostic>(command);
        }
    }

    public void SavePosition(AircraftPositionSample sample)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            using (var insertCommand = _connection.CreateCommand())
            {
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = """
                    INSERT INTO aircraft_positions (aircraft_identifier, recorded_at_utc, payload)
                    VALUES (@id, @recordedAtUtc, @payload);
                    """;
                insertCommand.Parameters.AddWithValue("@id", sample.AircraftIdentifier);
                insertCommand.Parameters.AddWithValue("@recordedAtUtc", sample.RecordedAtUtc.ToString("O"));
                insertCommand.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(sample, _jsonOptions));
                insertCommand.ExecuteNonQuery();
            }

            using (var trimCommand = _connection.CreateCommand())
            {
                trimCommand.Transaction = transaction;
                trimCommand.CommandText = """
                    DELETE FROM aircraft_positions
                    WHERE aircraft_identifier = @id
                    AND id NOT IN (
                        SELECT id FROM aircraft_positions
                        WHERE aircraft_identifier = @id
                        ORDER BY recorded_at_utc DESC
                        LIMIT @cap
                    );
                    """;
                trimCommand.Parameters.AddWithValue("@id", sample.AircraftIdentifier);
                trimCommand.Parameters.AddWithValue("@cap", MaximumPositionSamplesPerAircraft);
                trimCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public IReadOnlyList<AircraftPositionSample> GetPositionHistory(string aircraftIdentifier, int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumPositionSamplesPerAircraft);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT payload FROM aircraft_positions
                WHERE aircraft_identifier = @id
                ORDER BY recorded_at_utc DESC
                LIMIT @limit;
                """;
            command.Parameters.AddWithValue("@id", aircraftIdentifier);
            command.Parameters.AddWithValue("@limit", boundedLimit);

            var samples = ReadPayloads<AircraftPositionSample>(command);
            samples.Reverse();
            return samples;
        }
    }

    public bool SaveSondeTrack(SondeTelemetrySnapshot track)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sonde_tracks (serial, updated_at_utc, payload)
                VALUES (@serial, @updatedAtUtc, @payload)
                ON CONFLICT(serial) DO UPDATE SET
                    updated_at_utc = excluded.updated_at_utc,
                    payload = excluded.payload
                WHERE sonde_tracks.updated_at_utc < excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("@serial", track.Serial);
            command.Parameters.AddWithValue("@updatedAtUtc", track.UpdatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(track, _jsonOptions));
            return command.ExecuteNonQuery() > 0;
        }
    }

    public IReadOnlyList<SondeTelemetrySnapshot> GetCurrentSondeTracks(DateTimeOffset utcNow, TimeSpan staleAfter)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT payload FROM sonde_tracks
                WHERE updated_at_utc >= @cutoff
                ORDER BY serial;
                """;
            command.Parameters.AddWithValue("@cutoff", (utcNow - staleAfter).ToString("O"));

            return ReadPayloads<SondeTelemetrySnapshot>(command);
        }
    }

    public void SaveSondePosition(SondePositionSample sample)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            using (var insertCommand = _connection.CreateCommand())
            {
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = """
                    INSERT INTO sonde_positions (serial, recorded_at_utc, payload)
                    VALUES (@serial, @recordedAtUtc, @payload);
                    """;
                insertCommand.Parameters.AddWithValue("@serial", sample.Serial);
                insertCommand.Parameters.AddWithValue("@recordedAtUtc", sample.RecordedAtUtc.ToString("O"));
                insertCommand.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(sample, _jsonOptions));
                insertCommand.ExecuteNonQuery();
            }

            using (var trimCommand = _connection.CreateCommand())
            {
                trimCommand.Transaction = transaction;
                trimCommand.CommandText = """
                    DELETE FROM sonde_positions
                    WHERE serial = @serial
                    AND id NOT IN (
                        SELECT id FROM sonde_positions
                        WHERE serial = @serial
                        ORDER BY recorded_at_utc DESC
                        LIMIT @cap
                    );
                    """;
                trimCommand.Parameters.AddWithValue("@serial", sample.Serial);
                trimCommand.Parameters.AddWithValue("@cap", MaximumPositionSamplesPerAircraft);
                trimCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public IReadOnlyList<SondePositionSample> GetSondePositionHistory(string serial, int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumPositionSamplesPerAircraft);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT payload FROM sonde_positions
                WHERE serial = @serial
                ORDER BY recorded_at_utc DESC
                LIMIT @limit;
                """;
            command.Parameters.AddWithValue("@serial", serial);
            command.Parameters.AddWithValue("@limit", boundedLimit);

            var samples = ReadPayloads<SondePositionSample>(command);
            samples.Reverse();
            return samples;
        }
    }

    public StorageSnapshot GetSnapshot(DateTimeOffset checkedAtUtc)
    {
        lock (_gate)
        {
            return new StorageSnapshot(
                _databasePath,
                CountRows("messages"),
                CountRows("aircraft_tracks"),
                CountRows("import_diagnostics"),
                GetBytesUsed(),
                checkedAtUtc,
                CountRows("aircraft_positions"));
        }
    }

    public StorageRetentionResult ApplyRetention(StorageRetentionPolicy policy, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            var messageCutoff = utcNow.AddDays(-Math.Max(1, policy.MessageRetentionDays)).ToString("O");
            var trackCutoff = utcNow.AddDays(-Math.Max(1, policy.TrackRetentionDays)).ToString("O");
            var diagnosticCutoff = utcNow.AddDays(-Math.Max(1, policy.DiagnosticRetentionDays)).ToString("O");

            var messagesRemoved = DeleteWhereBefore("messages", "received_at_utc", messageCutoff);
            var tracksRemoved = DeleteWhereBefore("aircraft_tracks", "updated_at_utc", trackCutoff);
            var diagnosticsRemoved = DeleteWhereBefore("import_diagnostics", "occurred_at_utc", diagnosticCutoff);
            DeleteWhereBefore("aircraft_positions", "recorded_at_utc", trackCutoff);
            DeleteWhereBefore("sonde_tracks", "updated_at_utc", trackCutoff);
            DeleteWhereBefore("sonde_positions", "recorded_at_utc", trackCutoff);

            return new StorageRetentionResult(messagesRemoved, tracksRemoved, diagnosticsRemoved, utcNow);
        }
    }

    public StorageExportResult ExportSnapshot(string exportName, DateTimeOffset exportedAtUtc)
    {
        lock (_gate)
        {
            var safeName = string.Join("-", exportName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var exportPath = Path.Combine(Path.GetDirectoryName(_databasePath)!, "exports", string.IsNullOrWhiteSpace(safeName) ? "snapshot" : safeName);
            Directory.CreateDirectory(exportPath);

            var messageCount = ExportTable("messages", Path.Combine(exportPath, "messages.jsonl"));
            var trackCount = ExportTable("aircraft_tracks", Path.Combine(exportPath, "aircraft-tracks.jsonl"));
            var diagnosticCount = ExportTable("import_diagnostics", Path.Combine(exportPath, "import-diagnostics.jsonl"));
            ExportTable("aircraft_positions", Path.Combine(exportPath, "aircraft-positions.jsonl"));
            ExportTable("sonde_tracks", Path.Combine(exportPath, "sonde-tracks.jsonl"));
            ExportTable("sonde_positions", Path.Combine(exportPath, "sonde-positions.jsonl"));

            return new StorageExportResult(exportPath, messageCount, trackCount, diagnosticCount, exportedAtUtc);
        }
    }

    private int ExportTable(string tableName, string destinationPath)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {tableName};";
        using var reader = command.ExecuteReader();
        using var writer = new StreamWriter(destinationPath, append: false);
        var count = 0;

        while (reader.Read())
        {
            writer.WriteLine(reader.GetString(0));
            count++;
        }

        return count;
    }

    private long CountRows(string tableName)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private int DeleteWhereBefore(string tableName, string columnName, string cutoffIso)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"DELETE FROM {tableName} WHERE {columnName} < @cutoff;";
        command.Parameters.AddWithValue("@cutoff", cutoffIso);
        return command.ExecuteNonQuery();
    }

    private List<T> ReadPayloads<T>(SqliteCommand command)
    {
        var results = new List<T>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var record = JsonSerializer.Deserialize<T>(reader.GetString(0), _jsonOptions);

            if (record is not null)
            {
                results.Add(record);
            }
        }

        return results;
    }

    private long GetBytesUsed()
    {
        var total = 0L;

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _databasePath + suffix;

            if (File.Exists(path))
            {
                total += new FileInfo(path).Length;
            }
        }

        return total;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
