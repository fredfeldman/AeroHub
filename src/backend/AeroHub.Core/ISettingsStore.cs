using AeroHub.Contracts;

namespace AeroHub.Core;

public interface ISettingsStore
{
    event EventHandler<DecoderSettings>? SettingsChanged;

    DecoderSettings GetSettings();

    DecoderSettings UpdateSettings(DecoderSettings settings);
}
