using SpeechToText.Core.Models;
using SpeechToTextAmiVoiceMAUI.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpeechToTextAmiVoiceMAUI.ViewModels;

public sealed class SettingsPageViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore settingsStore;

    private string webSocketUri = ConnectionSettings.DefaultWebSocketUri;
    private string apiKey = "";
    private string httpPostUri = "";
    private string bouyomiHost = "127.0.0.1";
    private int bouyomiPort = 50001;
    private string bouyomiPrefix = "";
    private SpeechToTextSettings.BouyomiChanVoiceMapper selectedBouyomiVoice;
    private string statusText = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CloseRequested;

    public ObservableCollection<SpeechToTextSettings.BouyomiChanVoiceMapper> BouyomiVoiceItems { get; }

    public Command SaveCommand { get; }
    public Command CancelCommand { get; }

    public SettingsPageViewModel(ISettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
        BouyomiVoiceItems = new ObservableCollection<SpeechToTextSettings.BouyomiChanVoiceMapper>(SpeechToTextSettings.BouyomiChanVoiceMap);
        selectedBouyomiVoice = BouyomiVoiceItems.First();

        SaveCommand = new Command(Save);
        CancelCommand = new Command(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        Load();
    }

    public string WebSocketUri
    {
        get => webSocketUri;
        set => SetProperty(ref webSocketUri, value);
    }

    public string ApiKey
    {
        get => apiKey;
        set => SetProperty(ref apiKey, value);
    }

    public string HttpPostUri
    {
        get => httpPostUri;
        set => SetProperty(ref httpPostUri, value);
    }

    public string BouyomiHost
    {
        get => bouyomiHost;
        set => SetProperty(ref bouyomiHost, value);
    }

    public int BouyomiPort
    {
        get => bouyomiPort;
        set => SetProperty(ref bouyomiPort, value);
    }

    public string BouyomiPrefix
    {
        get => bouyomiPrefix;
        set => SetProperty(ref bouyomiPrefix, value);
    }

    public SpeechToTextSettings.BouyomiChanVoiceMapper SelectedBouyomiVoice
    {
        get => selectedBouyomiVoice;
        set => SetProperty(ref selectedBouyomiVoice, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    private void Load()
    {
        var settings = settingsStore.LoadConnectionSettings();
        WebSocketUri = settings.WebSocketUri;
        ApiKey = settings.ApiKey;
        HttpPostUri = settings.HttpPostUri;
        BouyomiHost = settings.BouyomiHost;
        BouyomiPort = settings.BouyomiPort;
        BouyomiPrefix = settings.BouyomiPrefix;

        var voice = BouyomiVoiceItems.FirstOrDefault(v => v.Tone == settings.BouyomiVoiceTone);
        if (voice is not null)
        {
            SelectedBouyomiVoice = voice;
        }
    }

    private void Save()
    {
        if (!TryValidateWebSocketUri(WebSocketUri))
        {
            StatusText = "WebSocket URI must start with ws:// or wss://.";
            return;
        }

        var settings = new ConnectionSettings
        {
            WebSocketUri = string.IsNullOrWhiteSpace(WebSocketUri) ? ConnectionSettings.DefaultWebSocketUri : WebSocketUri.Trim(),
            ApiKey = ApiKey?.Trim() ?? "",
            HttpPostUri = HttpPostUri?.Trim() ?? "",
            BouyomiHost = string.IsNullOrWhiteSpace(BouyomiHost) ? "127.0.0.1" : BouyomiHost.Trim(),
            BouyomiPort = BouyomiPort <= 0 ? 50001 : BouyomiPort,
            BouyomiPrefix = BouyomiPrefix ?? "",
            BouyomiVoiceTone = SelectedBouyomiVoice.Tone
        };

        settingsStore.SaveConnectionSettings(settings);
        StatusText = "Saved.";
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryValidateWebSocketUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == "ws" || uri.Scheme == "wss");
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
