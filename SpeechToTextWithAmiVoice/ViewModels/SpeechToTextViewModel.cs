using ReactiveUI;
using SpeechToText.Core;
using SpeechToText.Core.Models;
using SpeechToTextWithAmiVoice.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechToTextWithAmiVoice.ViewModels
{
    public class SpeechToTextViewModel : ViewModelBase
    {
        private const string FillerPattern = @"%(.*)%";
        private const string DeletePattern = @"%%";
        private const double WaveVolumeMinimum = -100.0;

        private readonly ISettingsStore settingsStore;
        private readonly RecognitionSessionCoordinator sessionCoordinator;
        private readonly RecognitionResultDispatcher resultDispatcher;

        private CancellationTokenSource? cancellationTokenSource;
        private bool isRecording;
        private bool isTransitioning;
        private bool suppressRuntimeSave;
        private string preferredAudioDeviceId = "";

        public ObservableCollection<AmiVoiceEngineItem> AmiVoiceEngineItems { get; }
        public ObservableCollection<AudioInputDevice> WaveInDeviceItems { get; }

        private AmiVoiceEngineItem selectedEngine;
        public AmiVoiceEngineItem SelectedEngine
        {
            get => selectedEngine;
            set
            {
                this.RaiseAndSetIfChanged(ref selectedEngine, value);
                SaveRuntimeOptions();
            }
        }

        private AudioInputDevice? selectedWaveInDevice;
        public AudioInputDevice? SelectedWaveInDevice
        {
            get => selectedWaveInDevice;
            set
            {
                this.RaiseAndSetIfChanged(ref selectedWaveInDevice, value);
                SaveRuntimeOptions();
            }
        }

        private string profileId = "";
        public string ProfileId
        {
            get => profileId;
            set
            {
                this.RaiseAndSetIfChanged(ref profileId, value);
                SaveRuntimeOptions();
            }
        }

        private bool fillerEnabled;
        public bool FillerEnabled
        {
            get => fillerEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref fillerEnabled, value);
                SaveRuntimeOptions();
            }
        }

        private bool enableHttpPost;
        public bool EnableHttpPost
        {
            get => enableHttpPost;
            set
            {
                this.RaiseAndSetIfChanged(ref enableHttpPost, value);
                SaveRuntimeOptions();
            }
        }

        private bool enableBouyomi;
        public bool EnableBouyomi
        {
            get => enableBouyomi;
            set
            {
                this.RaiseAndSetIfChanged(ref enableBouyomi, value);
                SaveRuntimeOptions();
            }
        }

        private string statusText = "Ready";
        public string StatusText
        {
            get => statusText;
            set => this.RaiseAndSetIfChanged(ref statusText, value);
        }

        private string recognizedText = "";
        public string RecognizedText
        {
            get => recognizedText;
            set => this.RaiseAndSetIfChanged(ref recognizedText, value);
        }

        private double waveMaxValue = WaveVolumeMinimum;
        public double WaveMaxValue
        {
            get => waveMaxValue;
            set => this.RaiseAndSetIfChanged(ref waveMaxValue, value);
        }

        private long droppedBackpressureCount;
        public long DroppedBackpressureCount
        {
            get => droppedBackpressureCount;
            set => this.RaiseAndSetIfChanged(ref droppedBackpressureCount, value);
        }

        private int connectionEstablishedCount;
        public int ConnectionEstablishedCount
        {
            get => connectionEstablishedCount;
            set => this.RaiseAndSetIfChanged(ref connectionEstablishedCount, value);
        }

        private string lastDisconnectReason = "N/A";
        public string LastDisconnectReason
        {
            get => lastDisconnectReason;
            set => this.RaiseAndSetIfChanged(ref lastDisconnectReason, value);
        }

        private string lastRecognizedAt = "-";
        public string LastRecognizedAt
        {
            get => lastRecognizedAt;
            set => this.RaiseAndSetIfChanged(ref lastRecognizedAt, value);
        }

        public bool IsRecording
        {
            get => isRecording;
            private set
            {
                this.RaiseAndSetIfChanged(ref isRecording, value);
                this.RaisePropertyChanged(nameof(CanEditSettings));
                this.RaisePropertyChanged(nameof(RecordButtonText));
            }
        }

        public bool CanEditSettings => !IsRecording && !isTransitioning;

        public string RecordButtonText => IsRecording ? "Stop" : "Start";

        public ReactiveCommand<Unit, Unit> ToggleRecordingCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }

        public event EventHandler? OpenSettingsRequested;

        public SpeechToTextViewModel(
            ISettingsStore settingsStore,
            RecognitionSessionCoordinator sessionCoordinator,
            RecognitionResultDispatcher resultDispatcher)
        {
            this.settingsStore = settingsStore;
            this.sessionCoordinator = sessionCoordinator;
            this.resultDispatcher = resultDispatcher;

            AmiVoiceEngineItems = new ObservableCollection<AmiVoiceEngineItem>(AmiVoiceAPI.PreDefinedEngines);
            selectedEngine = AmiVoiceEngineItems.First();
            WaveInDeviceItems = new ObservableCollection<AudioInputDevice>();

            ToggleRecordingCommand = ReactiveCommand.CreateFromTask(ToggleRecordingAsync);
            RefreshDevicesCommand = ReactiveCommand.Create(RefreshDevices);
            OpenSettingsCommand = ReactiveCommand.Create(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));

            sessionCoordinator.RecognizingTextUpdated += OnRecognizingTextUpdated;
            sessionCoordinator.Recognized += OnRecognized;
            sessionCoordinator.WaveLevelUpdated += (_, db) => WaveMaxValue = db;
            sessionCoordinator.DroppedBackpressureUpdated += (_, count) => DroppedBackpressureCount = count;
            sessionCoordinator.ConnectionEstablishedCountUpdated += (_, count) => ConnectionEstablishedCount = count;
            sessionCoordinator.LastDisconnectReasonUpdated += (_, reason) => LastDisconnectReason = reason;
            sessionCoordinator.StatusUpdated += (_, status) => StatusText = status;
            sessionCoordinator.RunningStateUpdated += (_, running) => IsRecording = running;

            suppressRuntimeSave = true;
            LoadRuntimeOptions();
            RefreshDevices();
            suppressRuntimeSave = false;
            this.RaisePropertyChanged(nameof(CanEditSettings));
            this.RaisePropertyChanged(nameof(RecordButtonText));
        }

        private async Task ToggleRecordingAsync()
        {
            if (isTransitioning)
            {
                return;
            }

            isTransitioning = true;
            this.RaisePropertyChanged(nameof(CanEditSettings));

            try
            {
                if (IsRecording)
                {
                    await StopRecordingAsync();
                }
                else
                {
                    await StartRecordingAsync();
                }
            }
            finally
            {
                isTransitioning = false;
                this.RaisePropertyChanged(nameof(CanEditSettings));
            }
        }

        private async Task StartRecordingAsync()
        {
            if (SelectedWaveInDevice is null)
            {
                StatusText = "No audio input device is selected.";
                return;
            }

            var connectionSettings = settingsStore.LoadConnectionSettings();
            if (string.IsNullOrWhiteSpace(connectionSettings.ApiKey))
            {
                StatusText = "API Key is required. Open Settings.";
                return;
            }

            var runtimeOptions = BuildRuntimeOptions();
            settingsStore.SaveRuntimeOptions(runtimeOptions);
            resultDispatcher.Configure(connectionSettings, runtimeOptions);

            cancellationTokenSource?.Dispose();
            cancellationTokenSource = new CancellationTokenSource();

            bool started;
            try
            {
                started = await sessionCoordinator.StartAsync(
                    connectionSettings,
                    runtimeOptions,
                    SelectedWaveInDevice,
                    cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
                IsRecording = false;
                return;
            }

            if (!started)
            {
                IsRecording = false;
                return;
            }

            IsRecording = true;
            DroppedBackpressureCount = 0;
        }

        private async Task StopRecordingAsync()
        {
            cancellationTokenSource?.Cancel();
            try
            {
                await sessionCoordinator.StopAsync();
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            WaveMaxValue = WaveVolumeMinimum;
            IsRecording = false;
        }

        private void RefreshDevices()
        {
            var previousId = SelectedWaveInDevice?.Id;
            var devices = sessionCoordinator.GetAvailableDevices();
            WaveInDeviceItems.Clear();
            foreach (var device in devices)
            {
                WaveInDeviceItems.Add(device);
            }

            var targetId = string.IsNullOrWhiteSpace(previousId) ? preferredAudioDeviceId : previousId;
            SelectedWaveInDevice = WaveInDeviceItems.FirstOrDefault(d => d.Id == targetId)
                ?? WaveInDeviceItems.FirstOrDefault();

            if (!WaveInDeviceItems.Any())
            {
                StatusText = "No audio input device is available.";
            }
        }

        private void OnRecognizingTextUpdated(object? sender, string text)
        {
            RecognizedText = text;
        }

        private void OnRecognized(object? sender, VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.code))
            {
                return;
            }

            var text = NormalizeRecognizedText(e.Text ?? "");
            RecognizedText = text;
            LastRecognizedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _ = resultDispatcher.DispatchAsync(text);
        }

        private string NormalizeRecognizedText(string text)
        {
            if (!FillerEnabled)
            {
                return text;
            }

            var normalized = Regex.Replace(text, FillerPattern, "$1");
            normalized = Regex.Replace(normalized, DeletePattern, "");
            return normalized;
        }

        private RuntimeOptions BuildRuntimeOptions()
        {
            return new RuntimeOptions
            {
                ProfileId = ProfileId?.Trim() ?? "",
                FillerEnabled = FillerEnabled,
                EngineConnectionId = string.IsNullOrWhiteSpace(SelectedEngine.ConnectionId) ? "-a-general" : SelectedEngine.ConnectionId,
                AudioDeviceId = SelectedWaveInDevice?.Id ?? "",
                EnableHttpPost = EnableHttpPost,
                EnableBouyomi = EnableBouyomi
            };
        }

        private void LoadRuntimeOptions()
        {
            var options = settingsStore.LoadRuntimeOptions();
            ProfileId = options.ProfileId;
            FillerEnabled = options.FillerEnabled;
            EnableHttpPost = options.EnableHttpPost;
            EnableBouyomi = options.EnableBouyomi;
            preferredAudioDeviceId = options.AudioDeviceId ?? "";

            var selected = AmiVoiceEngineItems.FirstOrDefault(e => e.ConnectionId == options.EngineConnectionId);
            if (!string.IsNullOrWhiteSpace(selected.ConnectionId))
            {
                SelectedEngine = selected;
            }
        }

        private void SaveRuntimeOptions()
        {
            if (suppressRuntimeSave)
            {
                return;
            }

            settingsStore.SaveRuntimeOptions(BuildRuntimeOptions());
        }
    }
}
