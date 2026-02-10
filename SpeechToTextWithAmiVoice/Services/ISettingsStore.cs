namespace SpeechToTextWithAmiVoice.Services;

public interface ISettingsStore
{
    ConnectionSettings LoadConnectionSettings();
    RuntimeOptions LoadRuntimeOptions();
    void SaveConnectionSettings(ConnectionSettings settings);
    void SaveRuntimeOptions(RuntimeOptions options);
}
