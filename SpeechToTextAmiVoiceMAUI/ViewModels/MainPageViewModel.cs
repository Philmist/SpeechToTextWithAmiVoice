using SpeechToText.Core;
using SpeechToText.Core.Models;
using SpeechToTextAmiVoiceMAUI.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SpeechToTextAmiVoiceMAUI.ViewModels;

public sealed class MainPageViewModel : INotifyPropertyChanged
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

    private string profileId = "";
    private bool fillerEnabled;
    private AmiVoiceEngineItem selectedEngine;
    private AudioInputDevice? selectedAudioDevice;
    private bool enableHttpPost;
    private bool enableBouyomi;
    private string recognizedText = "";
    private string statusText = "Ready";
    private double waveMaxValue = WaveVolumeMinimum;
    private long droppedBackpressureCount;
    private int connectionEstablishedCount;
    private string lastDisconnectReason = "N/A";
    private string lastRecognizedAt = "-";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AmiVoiceEngineItem> EngineItems { get; }
    public ObservableCollection<AudioInputDevice> AudioDevices { get; }

    public Command ToggleRecordingCommand { get; }
    public Command RefreshDevicesCommand { get; }

    public MainPageViewModel(
        ISettingsStore settingsStore,
        RecognitionSessionCoordinator sessionCoordinator,
        RecognitionResultDispatcher resultDispatcher)
    {
        this.settingsStore = settingsStore;
        this.sessionCoordinator = sessionCoordinator;
        this.resultDispatcher = resultDispatcher;

        EngineItems = new ObservableCollection<AmiVoiceEngineItem>(AmiVoiceAPI.PreDefinedEngines);
        selectedEngine = EngineItems.FirstOrDefault();
        AudioDevices = new ObservableCollection<AudioInputDevice>();

        ToggleRecordingCommand = new Command(async () => await ToggleRecordingAsync(), () => !isTransitioning);
        RefreshDevicesCommand = new Command(RefreshDevices, () => !isRecording && !isTransitioning);

        sessionCoordinator.RecognizingTextUpdated += OnRecognizingTextUpdated;
        sessionCoordinator.Recognized += OnRecognized;
        sessionCoordinator.WaveLevelUpdated += OnWaveLevelUpdated;
        sessionCoordinator.DroppedBackpressureUpdated += OnDroppedBackpressureUpdated;
        sessionCoordinator.ConnectionEstablishedCountUpdated += OnConnectionEstablishedCountUpdated;
        sessionCoordinator.LastDisconnectReasonUpdated += OnLastDisconnectReasonUpdated;
        sessionCoordinator.StatusUpdated += OnStatusUpdated;
        sessionCoordinator.RunningStateUpdated += OnRunningStateUpdated;

        LoadRuntimeOptions();
        RefreshDevices();
        UpdateComputedState();
    }

    public string ProfileId
    {
        get => profileId;
        set
        {
            if (SetProperty(ref profileId, value))
            {
                SaveRuntimeOptions();
            }
        }
    }

    public bool FillerEnabled
    {
        get => fillerEnabled;
        set
        {
            if (SetProperty(ref fillerEnabled, value))
            {
                SaveRuntimeOptions();
            }
        }
    }

    public AmiVoiceEngineItem SelectedEngine
    {
        get => selectedEngine;
        set
        {
            if (SetProperty(ref selectedEngine, value))
            {
                SaveRuntimeOptions();
            }
        }
    }

    public AudioInputDevice? SelectedAudioDevice
    {
        get => selectedAudioDevice;
        set
        {
            if (SetProperty(ref selectedAudioDevice, value))
            {
                SaveRuntimeOptions();
            }
        }
    }

    public bool EnableHttpPost
    {
        get => enableHttpPost;
        set
        {
            if (SetProperty(ref enableHttpPost, value))
            {
                SaveRuntimeOptions();
            }
        }
    }

    public bool EnableBouyomi
    {
        get => enableBouyomi;
        set
        {
            if (SetProperty(ref enableBouyomi, value))
            {
                SaveRuntimeOptions();
            }
        }
    }

    public string RecognizedText
    {
        get => recognizedText;
        private set => SetProperty(ref recognizedText, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public double WaveMaxValue
    {
        get => waveMaxValue;
        private set
        {
            if (SetProperty(ref waveMaxValue, value))
            {
                OnPropertyChanged(nameof(WaveProgress));
            }
        }
    }

    public double WaveProgress => Math.Clamp((WaveMaxValue - WaveVolumeMinimum) / (0 - WaveVolumeMinimum), 0.0, 1.0);

    public long DroppedBackpressureCount
    {
        get => droppedBackpressureCount;
        private set => SetProperty(ref droppedBackpressureCount, value);
    }

    public int ConnectionEstablishedCount
    {
        get => connectionEstablishedCount;
        private set => SetProperty(ref connectionEstablishedCount, value);
    }

    public string LastDisconnectReason
    {
        get => lastDisconnectReason;
        private set => SetProperty(ref lastDisconnectReason, value);
    }

    public string LastRecognizedAt
    {
        get => lastRecognizedAt;
        private set => SetProperty(ref lastRecognizedAt, value);
    }

    public bool IsRecording
    {
        get => isRecording;
        private set
        {
            if (SetProperty(ref isRecording, value))
            {
                UpdateComputedState();
                RefreshDevicesCommand.ChangeCanExecute();
            }
        }
    }

    public bool CanEditSettings => !IsRecording && !isTransitioning;

    public string RecordButtonText => IsRecording ? "Stop" : "Start";

    private async Task ToggleRecordingAsync()
    {
        if (isTransitioning)
        {
            return;
        }

        isTransitioning = true;
        ToggleRecordingCommand.ChangeCanExecute();
        RefreshDevicesCommand.ChangeCanExecute();
        OnPropertyChanged(nameof(CanEditSettings));

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
            ToggleRecordingCommand.ChangeCanExecute();
            RefreshDevicesCommand.ChangeCanExecute();
            OnPropertyChanged(nameof(CanEditSettings));
        }
    }

    private async Task StartRecordingAsync()
    {
        if (SelectedAudioDevice is null)
        {
            StatusText = "No audio input device is selected.";
            return;
        }

        var connectionSettings = settingsStore.LoadConnectionSettings();
        if (string.IsNullOrWhiteSpace(connectionSettings.ApiKey))
        {
            StatusText = "API Key is required. Open Connection Settings.";
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
                SelectedAudioDevice,
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
        var previousId = SelectedAudioDevice?.Id;
        var devices = sessionCoordinator.GetAvailableDevices();
        AudioDevices.Clear();
        foreach (var device in devices)
        {
            AudioDevices.Add(device);
        }

        SelectedAudioDevice = AudioDevices.FirstOrDefault(d => d.Id == previousId)
            ?? AudioDevices.FirstOrDefault();

        if (!AudioDevices.Any())
        {
            StatusText = "No audio input device is available.";
        }
    }

    private void OnRecognizingTextUpdated(object? sender, string text)
    {
        MainThread.BeginInvokeOnMainThread(() => RecognizedText = text);
    }

    private void OnRecognized(object? sender, VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.code))
        {
            return;
        }

        var text = NormalizeRecognizedText(e.Text ?? "");
        var recognizedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RecognizedText = text;
            LastRecognizedAt = recognizedAt;
        });
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

    private void OnWaveLevelUpdated(object? sender, double db)
    {
        MainThread.BeginInvokeOnMainThread(() => WaveMaxValue = db);
    }

    private void OnDroppedBackpressureUpdated(object? sender, long count)
    {
        MainThread.BeginInvokeOnMainThread(() => DroppedBackpressureCount = count);
    }

    private void OnConnectionEstablishedCountUpdated(object? sender, int count)
    {
        MainThread.BeginInvokeOnMainThread(() => ConnectionEstablishedCount = count);
    }

    private void OnLastDisconnectReasonUpdated(object? sender, string reason)
    {
        MainThread.BeginInvokeOnMainThread(() => LastDisconnectReason = reason);
    }

    private void OnStatusUpdated(object? sender, string status)
    {
        MainThread.BeginInvokeOnMainThread(() => StatusText = status);
    }

    private void OnRunningStateUpdated(object? sender, bool running)
    {
        MainThread.BeginInvokeOnMainThread(() => IsRecording = running);
    }

    private RuntimeOptions BuildRuntimeOptions()
    {
        return new RuntimeOptions
        {
            ProfileId = ProfileId?.Trim() ?? "",
            FillerEnabled = FillerEnabled,
            EngineConnectionId = string.IsNullOrWhiteSpace(SelectedEngine.ConnectionId) ? "-a-general" : SelectedEngine.ConnectionId,
            AudioDeviceId = SelectedAudioDevice?.Id ?? "",
            EnableHttpPost = EnableHttpPost,
            EnableBouyomi = EnableBouyomi
        };
    }

    private void LoadRuntimeOptions()
    {
        suppressRuntimeSave = true;
        try
        {
            var options = settingsStore.LoadRuntimeOptions();
            ProfileId = options.ProfileId;
            FillerEnabled = options.FillerEnabled;
            EnableHttpPost = options.EnableHttpPost;
            EnableBouyomi = options.EnableBouyomi;

            var selected = EngineItems.FirstOrDefault(e => e.ConnectionId == options.EngineConnectionId);
            if (!string.IsNullOrWhiteSpace(selected.ConnectionId))
            {
                SelectedEngine = selected;
            }
        }
        finally
        {
            suppressRuntimeSave = false;
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

    private void UpdateComputedState()
    {
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(RecordButtonText));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
