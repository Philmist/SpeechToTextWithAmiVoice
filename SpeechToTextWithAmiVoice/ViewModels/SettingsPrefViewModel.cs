using ReactiveUI;
using SpeechToText.Core.Models;
using SpeechToTextWithAmiVoice.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;

namespace SpeechToTextWithAmiVoice.ViewModels
{
    public class SettingsPrefViewModel : ViewModelBase
    {
        private readonly ISettingsStore settingsStore;

        private string webSocketUri;
        public string WebSocketUri
        {
            get => webSocketUri;
            set => this.RaiseAndSetIfChanged(ref webSocketUri, value);
        }

        private string apiKey;
        public string ApiKey
        {
            get => apiKey;
            set => this.RaiseAndSetIfChanged(ref apiKey, value);
        }

        private string httpPostUri;
        public string HttpPostUri
        {
            get => httpPostUri;
            set => this.RaiseAndSetIfChanged(ref httpPostUri, value);
        }

        private string bouyomiHost;
        public string BouyomiHost
        {
            get => bouyomiHost;
            set => this.RaiseAndSetIfChanged(ref bouyomiHost, value);
        }

        private int bouyomiPort;
        public int BouyomiPort
        {
            get => bouyomiPort;
            set => this.RaiseAndSetIfChanged(ref bouyomiPort, value);
        }

        private string bouyomiPrefix;
        public string BouyomiPrefix
        {
            get => bouyomiPrefix;
            set => this.RaiseAndSetIfChanged(ref bouyomiPrefix, value);
        }

        public ObservableCollection<SpeechToTextSettings.BouyomiChanVoiceMapper> BouyomiVoiceItems { get; }

        private SpeechToTextSettings.BouyomiChanVoiceMapper selectedVoice;
        public SpeechToTextSettings.BouyomiChanVoiceMapper SelectedVoice
        {
            get => selectedVoice;
            set => this.RaiseAndSetIfChanged(ref selectedVoice, value);
        }

        private string statusText;
        public string StatusText
        {
            get => statusText;
            set => this.RaiseAndSetIfChanged(ref statusText, value);
        }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public event EventHandler? CloseRequested;

        public SettingsPrefViewModel(ISettingsStore settingsStore)
        {
            this.settingsStore = settingsStore;

            webSocketUri = ConnectionSettings.DefaultWebSocketUri;
            apiKey = "";
            httpPostUri = "";
            bouyomiHost = "127.0.0.1";
            bouyomiPort = 50001;
            bouyomiPrefix = "";
            statusText = "";

            BouyomiVoiceItems = new ObservableCollection<SpeechToTextSettings.BouyomiChanVoiceMapper>(SpeechToTextSettings.BouyomiChanVoiceMap);
            selectedVoice = BouyomiVoiceItems.First();

            SaveCommand = ReactiveCommand.Create(Save);
            CancelCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke(this, EventArgs.Empty));

            Load();
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
                SelectedVoice = voice;
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
                BouyomiVoiceTone = SelectedVoice.Tone
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
    }
}
