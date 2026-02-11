using SpeechToText.Core;
using SpeechToText.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SpeechToTextWithAmiVoice.Services;

public sealed class RecognitionSessionCoordinator : IDisposable
{
    private const double WaveVolumeMinimum = -100.0;

    private readonly IAudioCaptureServiceFactory audioCaptureFactory;
    private readonly SemaphoreSlim lifecycleSemaphore = new(1, 1);

    private IAudioCaptureService? captureService;
    private VoiceRecognizerWithAmiVoiceCloud? recognizer;
    private IDisposable? audioSubscription;
    private CancellationTokenSource? cancellationTokenSource;
    private int connectionEstablishedCount;
    private VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType lastProvidingState = VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Initialized;
    private bool disposed;

    public bool IsRunning { get; private set; }

    public event EventHandler<string>? RecognizingTextUpdated;
    public event EventHandler<VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs>? Recognized;
    public event EventHandler<double>? WaveLevelUpdated;
    public event EventHandler<long>? DroppedBackpressureUpdated;
    public event EventHandler<int>? ConnectionEstablishedCountUpdated;
    public event EventHandler<string>? LastDisconnectReasonUpdated;
    public event EventHandler<string>? StatusUpdated;
    public event EventHandler<bool>? RunningStateUpdated;

    public RecognitionSessionCoordinator(IAudioCaptureServiceFactory audioCaptureFactory)
    {
        this.audioCaptureFactory = audioCaptureFactory;
    }

    public IReadOnlyList<AudioInputDevice> GetAvailableDevices()
    {
        return audioCaptureFactory.GetAvailableDevices();
    }

    public async Task<bool> StartAsync(
        ConnectionSettings connectionSettings,
        RuntimeOptions runtimeOptions,
        AudioInputDevice selectedAudioDevice,
        CancellationToken cancellationToken)
    {
        await lifecycleSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                return true;
            }

            var api = new AmiVoiceAPI
            {
                WebSocketURI = string.IsNullOrWhiteSpace(connectionSettings.WebSocketUri)
                    ? ConnectionSettings.DefaultWebSocketUri
                    : connectionSettings.WebSocketUri.Trim(),
                AppKey = connectionSettings.ApiKey?.Trim() ?? "",
                ProfileId = runtimeOptions.ProfileId?.Trim() ?? "",
                EngineName = string.IsNullOrWhiteSpace(runtimeOptions.EngineConnectionId)
                    ? "-a-general"
                    : runtimeOptions.EngineConnectionId,
                FillerEnable = runtimeOptions.FillerEnabled
            };

            recognizer = new VoiceRecognizerWithAmiVoiceCloud(api);
            lastProvidingState = VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Initialized;
            if (!string.IsNullOrWhiteSpace(api.ProfileId))
            {
                recognizer.ConnectionParameter["profileId"] = api.ProfileId;
            }
            if (api.FillerEnable)
            {
                recognizer.ConnectionParameter["keepFillerToken"] = "1";
            }

            SubscribeRecognizer(recognizer);

            cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            recognizer.Start(cancellationTokenSource.Token);
            var connected = await WaitForRecognizerReadyAsync(recognizer, cancellationTokenSource.Token, TimeSpan.FromSeconds(5));
            if (!connected)
            {
                var errorText = string.IsNullOrWhiteSpace(recognizer.LastErrorString)
                    ? "Failed to connect."
                    : recognizer.LastErrorString;
                await StopCoreAsync(fromRecognizeStopped: false, publishStoppedStatus: false);
                StatusUpdated?.Invoke(this, errorText);
                return false;
            }

            captureService = audioCaptureFactory.Create(selectedAudioDevice);
            captureService.ResampledMaxValueAvailable += OnWaveMaxValueAvailable;
            audioSubscription = captureService.Pcm16StreamObservable.Subscribe(new AudioObserver<ReadOnlyMemory<byte>>(buffer =>
            {
                _ = recognizer.TryFeedRawWave(buffer.Span);
            }));
            captureService.StartRecording();

            IsRunning = true;
            RunningStateUpdated?.Invoke(this, true);
            DroppedBackpressureUpdated?.Invoke(this, 0);
            StatusUpdated?.Invoke(this, "Streaming audio...");
            return true;
        }
        finally
        {
            lifecycleSemaphore.Release();
        }
    }

    public async Task StopAsync()
    {
        await lifecycleSemaphore.WaitAsync();
        try
        {
            await StopCoreAsync(fromRecognizeStopped: false, publishStoppedStatus: true);
        }
        finally
        {
            lifecycleSemaphore.Release();
        }
    }

    private async Task StopCoreAsync(bool fromRecognizeStopped, bool publishStoppedStatus)
    {
        cancellationTokenSource?.Cancel();
        if (recognizer is not null)
        {
            try
            {
                await recognizer.StopAsync(TimeSpan.FromSeconds(1));
            }
            catch
            {
            }
        }

        audioSubscription?.Dispose();
        audioSubscription = null;

        if (captureService is not null)
        {
            captureService.ResampledMaxValueAvailable -= OnWaveMaxValueAvailable;
            try
            {
                captureService.StopRecording();
            }
            catch
            {
            }

            captureService.Dispose();
            captureService = null;
        }

        if (recognizer is not null)
        {
            UnsubscribeRecognizer(recognizer);
            recognizer = null;
        }

        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
        lastProvidingState = VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Initialized;

        if (IsRunning)
        {
            IsRunning = false;
            RunningStateUpdated?.Invoke(this, false);
        }

        if (publishStoppedStatus && !fromRecognizeStopped)
        {
            StatusUpdated?.Invoke(this, "Recording stopped.");
        }
    }

    private void SubscribeRecognizer(VoiceRecognizerWithAmiVoiceCloud targetRecognizer)
    {
        targetRecognizer.Recognizing += OnRecognizing;
        targetRecognizer.Recognized += OnRecognized;
        targetRecognizer.ErrorOccured += OnRecognizerError;
        targetRecognizer.Trace += OnRecognizerTrace;
        targetRecognizer.RecognizeStopped += OnRecognizeStopped;
        targetRecognizer.StateChanged += OnRecognizerStateChanged;
        targetRecognizer.Disconnected += OnRecognizerDisconnected;
        targetRecognizer.AudioFeedStatsChanged += OnRecognizerAudioFeedStatsChanged;
    }

    private void UnsubscribeRecognizer(VoiceRecognizerWithAmiVoiceCloud targetRecognizer)
    {
        targetRecognizer.Recognizing -= OnRecognizing;
        targetRecognizer.Recognized -= OnRecognized;
        targetRecognizer.ErrorOccured -= OnRecognizerError;
        targetRecognizer.Trace -= OnRecognizerTrace;
        targetRecognizer.RecognizeStopped -= OnRecognizeStopped;
        targetRecognizer.StateChanged -= OnRecognizerStateChanged;
        targetRecognizer.Disconnected -= OnRecognizerDisconnected;
        targetRecognizer.AudioFeedStatsChanged -= OnRecognizerAudioFeedStatsChanged;
    }

    private void OnRecognizing(object? sender, VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Text))
        {
            RecognizingTextUpdated?.Invoke(this, e.Text);
        }
    }

    private void OnRecognized(object? sender, VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs e)
    {
        Recognized?.Invoke(this, e);
    }

    private void OnRecognizerError(object? sender, string error)
    {
        StatusUpdated?.Invoke(this, error);
    }

    private void OnRecognizerTrace(object? sender, string trace)
    {
        if (string.IsNullOrWhiteSpace(trace))
        {
            return;
        }

        Debug.WriteLine($"AmiVoice Trace: {trace.Trim()}");
    }

    private void OnRecognizerStateChanged(object? sender, VoiceRecognizerWithAmiVoiceCloud.StateSnapshot state)
    {
        if (state.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started &&
            lastProvidingState != VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started &&
            lastProvidingState != VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing)
        {
            connectionEstablishedCount++;
            ConnectionEstablishedCountUpdated?.Invoke(this, connectionEstablishedCount);
        }

        lastProvidingState = state.ProvidingState;
        var status = state.ProvidingState switch
        {
            VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Starting => "Connecting...",
            VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started => "Connected.",
            VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing => "Streaming audio...",
            VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Ending => "Stopping...",
            VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Error => "Recognizer error.",
            VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Initialized => IsRecognizerSessionActive()
                ? "Reconnecting..."
                : "Ready",
            _ => "Ready"
        };
        StatusUpdated?.Invoke(this, status);
    }

    private void OnRecognizerDisconnected(object? sender, VoiceRecognizerWithAmiVoiceCloud.DisconnectInfo disconnectInfo)
    {
        var reasonText = disconnectInfo.IsRecoverable
            ? $"{disconnectInfo.Reason} (Recoverable)"
            : disconnectInfo.Reason.ToString();
        LastDisconnectReasonUpdated?.Invoke(this, reasonText);

        var status = disconnectInfo.Reason switch
        {
            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.UserRequestedStop => "Stopping...",
            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerClosed => disconnectInfo.IsRecoverable
                ? "Connection lost. Reconnecting..."
                : "Connection closed by server.",
            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerTimeout => disconnectInfo.IsRecoverable
                ? "Server timeout. Reconnecting..."
                : "Server timeout.",
            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerEndedSession => disconnectInfo.IsRecoverable
                ? "Session ended. Reconnecting..."
                : "Session ended by server.",
            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.AuthFailed => "Authentication failed.",
            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.TransportError => disconnectInfo.IsRecoverable
                ? "Network error. Reconnecting..."
                : "Network error.",
            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ReconnectLimitReached => "Reconnect limit reached.",
            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.UnexpectedProtocol => "Protocol error.",
            _ => "Disconnected."
        };

        StatusUpdated?.Invoke(this, status);
    }

    private void OnRecognizerAudioFeedStatsChanged(object? sender, VoiceRecognizerWithAmiVoiceCloud.AudioFeedStats stats)
    {
        DroppedBackpressureUpdated?.Invoke(this, stats.DroppedBackpressure);
    }

    private void OnRecognizeStopped(object? sender, bool isStopped)
    {
        _ = StopFromRecognizerAsync();
    }

    private async Task StopFromRecognizerAsync()
    {
        await lifecycleSemaphore.WaitAsync();
        try
        {
            await StopCoreAsync(fromRecognizeStopped: true, publishStoppedStatus: true);
            StatusUpdated?.Invoke(this, "Recording stopped.");
        }
        finally
        {
            lifecycleSemaphore.Release();
        }
    }

    private void OnWaveMaxValueAvailable(object? sender, float value)
    {
        const double refDb = 1.0;
        var normalized = value / 32767.0;
        var db = normalized > 0 ? 20 * Math.Log10(normalized / refDb) : WaveVolumeMinimum;
        WaveLevelUpdated?.Invoke(this, db);
    }

    private bool IsRecognizerSessionActive()
    {
        return recognizer?.messageLoopTask is Task loopTask &&
               !loopTask.IsCompleted &&
               cancellationTokenSource?.IsCancellationRequested != true;
    }

    private static async Task<bool> WaitForRecognizerReadyAsync(
        VoiceRecognizerWithAmiVoiceCloud activeRecognizer,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var startAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startAt < timeout && !cancellationToken.IsCancellationRequested)
        {
            var providingState = activeRecognizer.CurrentState.ProvidingState;
            if (providingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started ||
                providingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing)
            {
                return true;
            }

            if (providingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Error)
            {
                return false;
            }

            try
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifecycleSemaphore.Wait();
        try
        {
            StopCoreAsync(fromRecognizeStopped: false, publishStoppedStatus: false).GetAwaiter().GetResult();
        }
        finally
        {
            lifecycleSemaphore.Release();
        }
        lifecycleSemaphore.Dispose();
    }

    private sealed class AudioObserver<T> : IObserver<T>
    {
        private readonly Action<T> onNext;

        public AudioObserver(Action<T> onNext)
        {
            this.onNext = onNext;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
        {
            onNext(value);
        }
    }
}
