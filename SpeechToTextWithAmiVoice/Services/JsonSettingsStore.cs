using System.Text.Json;
using System;
using System.IO;

namespace SpeechToTextWithAmiVoice.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string settingsPath;

    public JsonSettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDir = Path.Combine(appData, "SpeechToTextWithAmiVoice");
        Directory.CreateDirectory(appDir);
        settingsPath = Path.Combine(appDir, "settings.json");
    }

    public ConnectionSettings LoadConnectionSettings()
    {
        var root = LoadRoot();
        return root.Connection;
    }

    public RuntimeOptions LoadRuntimeOptions()
    {
        var root = LoadRoot();
        return root.Runtime;
    }

    public void SaveConnectionSettings(ConnectionSettings settings)
    {
        var root = LoadRoot();
        root.Connection = settings;
        SaveRoot(root);
    }

    public void SaveRuntimeOptions(RuntimeOptions options)
    {
        var root = LoadRoot();
        root.Runtime = options;
        SaveRoot(root);
    }

    private RootSettings LoadRoot()
    {
        if (!File.Exists(settingsPath))
        {
            return new RootSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new RootSettings();
            }

            return JsonSerializer.Deserialize<RootSettings>(json, serializerOptions) ?? new RootSettings();
        }
        catch
        {
            return new RootSettings();
        }
    }

    private void SaveRoot(RootSettings root)
    {
        var json = JsonSerializer.Serialize(root, serializerOptions);
        File.WriteAllText(settingsPath, json);
    }

    private sealed class RootSettings
    {
        public ConnectionSettings Connection { get; set; } = new();
        public RuntimeOptions Runtime { get; set; } = new();
    }
}
