using Microsoft.Maui.Storage;

namespace SpeechToTextAmiVoiceMAUI.Services;

public sealed class SettingsStore : ISettingsStore
{
    private const string PrefApiKey = "api_key";
    private const string PrefWebSocketUri = "ws_uri";
    private const string PrefProfileId = "profile_id";
    private const string PrefFillerEnabled = "filler_enabled";
    private const string PrefEngineId = "engine_id";
    private const string PrefAudioDeviceId = "audio_device_id";
    private const string PrefEnableHttpPost = "enable_http_post";
    private const string PrefHttpPostUri = "http_post_uri";
    private const string PrefEnableBouyomi = "enable_bouyomi";
    private const string PrefBouyomiHost = "bouyomi_host";
    private const string PrefBouyomiPort = "bouyomi_port";
    private const string PrefBouyomiPrefix = "bouyomi_prefix";
    private const string PrefBouyomiVoice = "bouyomi_voice";

    public ConnectionSettings LoadConnectionSettings()
    {
        return new ConnectionSettings
        {
            WebSocketUri = Preferences.Get(PrefWebSocketUri, ConnectionSettings.DefaultWebSocketUri),
            ApiKey = Preferences.Get(PrefApiKey, ""),
            HttpPostUri = Preferences.Get(PrefHttpPostUri, ""),
            BouyomiHost = Preferences.Get(PrefBouyomiHost, "127.0.0.1"),
            BouyomiPort = Preferences.Get(PrefBouyomiPort, 50001),
            BouyomiPrefix = Preferences.Get(PrefBouyomiPrefix, ""),
            BouyomiVoiceTone = (short)Preferences.Get(PrefBouyomiVoice, -1)
        };
    }

    public RuntimeOptions LoadRuntimeOptions()
    {
        return new RuntimeOptions
        {
            ProfileId = Preferences.Get(PrefProfileId, ""),
            FillerEnabled = Preferences.Get(PrefFillerEnabled, false),
            EngineConnectionId = Preferences.Get(PrefEngineId, "-a-general"),
            AudioDeviceId = Preferences.Get(PrefAudioDeviceId, ""),
            EnableHttpPost = Preferences.Get(PrefEnableHttpPost, false),
            EnableBouyomi = Preferences.Get(PrefEnableBouyomi, false)
        };
    }

    public void SaveConnectionSettings(ConnectionSettings settings)
    {
        Preferences.Set(PrefWebSocketUri, settings.WebSocketUri ?? ConnectionSettings.DefaultWebSocketUri);
        Preferences.Set(PrefApiKey, settings.ApiKey ?? "");
        Preferences.Set(PrefHttpPostUri, settings.HttpPostUri ?? "");
        Preferences.Set(PrefBouyomiHost, settings.BouyomiHost ?? "127.0.0.1");
        Preferences.Set(PrefBouyomiPort, settings.BouyomiPort);
        Preferences.Set(PrefBouyomiPrefix, settings.BouyomiPrefix ?? "");
        Preferences.Set(PrefBouyomiVoice, settings.BouyomiVoiceTone);
    }

    public void SaveRuntimeOptions(RuntimeOptions options)
    {
        Preferences.Set(PrefProfileId, options.ProfileId ?? "");
        Preferences.Set(PrefFillerEnabled, options.FillerEnabled);
        Preferences.Set(PrefEngineId, options.EngineConnectionId ?? "-a-general");
        Preferences.Set(PrefAudioDeviceId, options.AudioDeviceId ?? "");
        Preferences.Set(PrefEnableHttpPost, options.EnableHttpPost);
        Preferences.Set(PrefEnableBouyomi, options.EnableBouyomi);
    }
}
