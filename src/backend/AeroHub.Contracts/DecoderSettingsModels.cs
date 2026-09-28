using System.Text.Json.Serialization;

namespace AeroHub.Contracts;

public sealed record WefaxDecoderSettings(
    bool IsInverted = false,
    double SlantCorrection = 0.0,
    int Ioc = 576,
    int LineRateRpm = 120);

public sealed record Dump1090Settings(
    string? Host = null,
    int Port = 8080,
    string JsonPath = "/data/aircraft.json",
    int PollIntervalSeconds = 5,
    bool AutoConnect = false);

public sealed record FeederSettings(
    string FeederId,
    string Host,
    int Port,
    string StationId,
    bool AutoStart = false);

public sealed record HardwareSourceSettings(
    string SourceId,
    double? FrequencyMHz,
    double? SampleRateHz,
    double? GainDb,
    bool AgcEnabled);

public sealed record LocalFrequencyProfileSetting(
    string Id,
    string Name,
    double FrequencyMHz,
    IReadOnlyList<string> Tags);

public sealed record DecoderSettings(
    WefaxDecoderSettings Wefax,
    Dump1090Settings Dump1090,
    IReadOnlyList<FeederSettings> Feeders,
    IReadOnlyList<HardwareSourceSettings> HardwareSources,
    IReadOnlyList<LocalFrequencyProfileSetting> FrequencyProfiles,
    int StaleTrackTimeoutMinutes = 15,
    string MinimumConfidenceFilter = "Unknown")
{
    public DecoderSettings() : this(
        Wefax: new WefaxDecoderSettings(),
        Dump1090: new Dump1090Settings(),
        Feeders:
        [
            new FeederSettings("flightaware-beast-outbound", "feed.flightaware.com", 30005, "KDFW-FA-1", false),
            new FeederSettings("flightaware-basestation-outbound", "feed.flightaware.com", 30003, "KDFW-FA-1", false),
            new FeederSettings("flightaware-local-beast-server", "0.0.0.0", 30005, "KDFW-LOCAL-1", false)
        ],
        HardwareSources:
        [
            new HardwareSourceSettings("file-audio-iq", 136.800, 48000, 0.0, false),
            new HardwareSourceSettings("rtl-tcp-local", 1090.000, 2400000, 24.0, true)
        ],
        FrequencyProfiles:
        [
            new LocalFrequencyProfileSetting("local-acars-136800", "Local ACARS 136.800 MHz", 136.800, ["vhf-acars", "local-profile"]),
            new LocalFrequencyProfileSetting("local-acars-136975", "Local ACARS 136.975 MHz", 136.975, ["vhf-acars", "local-profile"]),
            new LocalFrequencyProfileSetting("local-acars-136650", "Local ACARS 136.650 MHz", 136.650, ["vhf-acars", "local-profile"])
        ],
        StaleTrackTimeoutMinutes: 15,
        MinimumConfidenceFilter: "Unknown")
    {
    }
}
