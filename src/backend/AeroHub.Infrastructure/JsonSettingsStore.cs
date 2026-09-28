using System.Text.Json;
using System.Text.Json.Serialization;
using AeroHub.Contracts;
using AeroHub.Core;

namespace AeroHub.Infrastructure;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly object _gate = new();
    private readonly string _primaryPath;
    private readonly string _backupPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private DecoderSettings _settings;

    public event EventHandler<DecoderSettings>? SettingsChanged;

    public JsonSettingsStore()
        : this(Path.Combine(AppContext.BaseDirectory, "data", "store"))
    {
    }

    public JsonSettingsStore(string rootPath)
    {
        Directory.CreateDirectory(rootPath);
        _primaryPath = Path.Combine(rootPath, "settings.json");
        _backupPath = Path.Combine(rootPath, "settings.json.backup");

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());

        _settings = LoadWithFallback();
    }

    public DecoderSettings GetSettings()
    {
        lock (_gate)
        {
            return _settings;
        }
    }

    public DecoderSettings UpdateSettings(DecoderSettings settings)
    {
        lock (_gate)
        {
            _settings = SanitizeSettings(settings);
            SaveToDisk(_settings);
        }

        SettingsChanged?.Invoke(this, _settings);
        return _settings;
    }

    private DecoderSettings LoadWithFallback()
    {
        if (File.Exists(_primaryPath))
        {
            try
            {
                var content = File.ReadAllText(_primaryPath);
                var loaded = JsonSerializer.Deserialize<DecoderSettings>(content, _jsonOptions);
                if (loaded is not null)
                {
                    var sanitized = SanitizeSettings(loaded);
                    File.Copy(_primaryPath, _backupPath, overwrite: true);
                    return sanitized;
                }
            }
            catch
            {
                // Primary file corrupt, try backup
            }
        }

        if (File.Exists(_backupPath))
        {
            try
            {
                var content = File.ReadAllText(_backupPath);
                var loaded = JsonSerializer.Deserialize<DecoderSettings>(content, _jsonOptions);
                if (loaded is not null)
                {
                    var sanitized = SanitizeSettings(loaded);
                    File.Copy(_backupPath, _primaryPath, overwrite: true);
                    return sanitized;
                }
            }
            catch
            {
                // Backup also corrupt
            }
        }

        var defaults = new DecoderSettings();
        SaveToDisk(defaults);
        return defaults;
    }

    private void SaveToDisk(DecoderSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_primaryPath, json);
        File.Copy(_primaryPath, _backupPath, overwrite: true);
    }

    private static DecoderSettings SanitizeSettings(DecoderSettings input)
    {
        var defaults = new DecoderSettings();
        return new DecoderSettings(
            Wefax: input.Wefax ?? defaults.Wefax,
            Dump1090: input.Dump1090 ?? defaults.Dump1090,
            Feeders: input.Feeders ?? defaults.Feeders,
            HardwareSources: input.HardwareSources ?? defaults.HardwareSources,
            FrequencyProfiles: input.FrequencyProfiles ?? defaults.FrequencyProfiles,
            StaleTrackTimeoutMinutes: input.StaleTrackTimeoutMinutes > 0 ? input.StaleTrackTimeoutMinutes : 15,
            MinimumConfidenceFilter: string.IsNullOrWhiteSpace(input.MinimumConfidenceFilter) ? "Unknown" : input.MinimumConfidenceFilter
        );
    }
}
