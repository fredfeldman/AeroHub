using System.IO.Compression;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

/// <summary>
/// Local, offline hex-to-registration/type enrichment backed by the free tar1090-db
/// (wiedehopf/tar1090-db, built from the Mictronics aircraft database) CSV export.
/// </summary>
public sealed class AircraftRegistryLookup : IAircraftRegistryLookup
{
    private readonly object _gate = new();
    private Dictionary<string, AircraftRegistryEntry>? _entries;

    public bool TryLookup(string hexAddress, out AircraftRegistryEntry entry)
    {
        var entries = _entries ?? LoadEntries();

        return entries.TryGetValue(hexAddress.ToUpperInvariant(), out entry);
    }

    private Dictionary<string, AircraftRegistryEntry> LoadEntries()
    {
        lock (_gate)
        {
            if (_entries is not null)
            {
                return _entries;
            }

            var entries = new Dictionary<string, AircraftRegistryEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var path = FindDatabasePath();
                using var fileStream = File.OpenRead(path);
                using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                using var reader = new StreamReader(gzipStream);

                string? line;

                while ((line = reader.ReadLine()) is not null)
                {
                    var fields = line.Split(';');

                    if (fields.Length < 2 || fields[0].Length == 0)
                    {
                        continue;
                    }

                    var hex = fields[0].ToUpperInvariant();
                    var registration = fields.Length > 1 && fields[1].Length > 0 ? fields[1] : null;
                    var icaoTypeCode = fields.Length > 2 && fields[2].Length > 0 ? fields[2] : null;
                    var isMilitary = fields.Length > 3 && int.TryParse(fields[3], out var dbFlags) && (dbFlags & 1) != 0;
                    var operatorName = fields.Length > 6 && fields[6].Length > 0 ? fields[6] : null;

                    if (registration is null && icaoTypeCode is null && operatorName is null)
                    {
                        continue;
                    }

                    entries[hex] = new AircraftRegistryEntry(registration, icaoTypeCode, operatorName, isMilitary);
                }
            }
            catch (IOException)
            {
                // Database is optional; enrichment simply stays unavailable.
            }

            _entries = entries;
            return entries;
        }
    }

    private static string FindDatabasePath()
    {
        var directory = AppContext.BaseDirectory;

        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "fixtures", "aircraft-db", "aircraft.csv.gz");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not find fixtures/aircraft-db/aircraft.csv.gz.");
    }
}
