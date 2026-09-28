using AeroHub.Contracts;
using AeroHub.Infrastructure;

namespace AeroHub.Api.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void JsonSettingsStore_creates_defaults_and_persists_updates()
    {
        using var testDirectory = new TemporaryDirectory();
        var store = new JsonSettingsStore(testDirectory.Path);

        var initial = store.GetSettings();
        Assert.Equal(576, initial.Wefax.Ioc);
        Assert.Equal(120, initial.Wefax.LineRateRpm);
        Assert.False(initial.Wefax.IsInverted);

        var updated = store.UpdateSettings(initial with
        {
            Wefax = initial.Wefax with { IsInverted = true, SlantCorrection = 1.5 },
            StaleTrackTimeoutMinutes = 20
        });

        Assert.True(updated.Wefax.IsInverted);
        Assert.Equal(1.5, updated.Wefax.SlantCorrection);
        Assert.Equal(20, updated.StaleTrackTimeoutMinutes);

        var reloaded = new JsonSettingsStore(testDirectory.Path);
        var reloadedSettings = reloaded.GetSettings();

        Assert.True(reloadedSettings.Wefax.IsInverted);
        Assert.Equal(1.5, reloadedSettings.Wefax.SlantCorrection);
        Assert.Equal(20, reloadedSettings.StaleTrackTimeoutMinutes);
    }

    [Fact]
    public void JsonSettingsStore_falls_back_to_backup_if_primary_is_corrupt()
    {
        using var testDirectory = new TemporaryDirectory();
        var store = new JsonSettingsStore(testDirectory.Path);

        store.UpdateSettings(store.GetSettings() with
        {
            Wefax = new WefaxDecoderSettings(IsInverted: true, SlantCorrection: -2.0, Ioc: 576, LineRateRpm: 120)
        });

        var primaryPath = Path.Combine(testDirectory.Path, "settings.json");
        var backupPath = Path.Combine(testDirectory.Path, "settings.json.backup");

        Assert.True(File.Exists(primaryPath));
        Assert.True(File.Exists(backupPath));

        // Corrupt primary file
        File.WriteAllText(primaryPath, "{ corrupt json ... ");

        var reloaded = new JsonSettingsStore(testDirectory.Path);
        var settings = reloaded.GetSettings();

        Assert.True(settings.Wefax.IsInverted);
        Assert.Equal(-2.0, settings.Wefax.SlantCorrection);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aerohub-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
