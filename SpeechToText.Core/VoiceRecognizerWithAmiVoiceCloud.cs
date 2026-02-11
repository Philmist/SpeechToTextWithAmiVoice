#nullable enable

using SpeechToText.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeechToText.Core;

public class VoiceRecognizerWithAmiVoiceCloud
{
    private readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private Uri connectionUri;
    private string appKey;
    private string engine;
    private string? profileId;
    private Dictionary<string, string> connectionParameter;
    private AmiVoiceSession? session;

    public enum ProvidingStateType
    {
        Initialized,
        Starting,
        Started,
        Providing,
        Ending,
        Error
    }

    public enum DetectingStateType
    {
        NotDetecting,
        Detecting
    }

    public enum RecognizingStateType
    {
        NotRecognizing,
        Recognizing
    }

    public enum AudioFeedResult
    {
        Accepted,
        DroppedNotReady,
        DroppedBackpressure,
        DroppedClosed
    }

    public enum DisconnectionReason
    {
        None,
        UserRequestedStop,
        ServerClosed,
        ServerTimeout,
        ServerEndedSession,
        AuthFailed,
        TransportError,
        ReconnectLimitReached,
        UnexpectedProtocol
    }

    public readonly record struct StateSnapshot(
        ProvidingStateType ProvidingState,
        DetectingStateType DetectingState,
        RecognizingStateType RecognizingState);

    public readonly record struct DisconnectInfo(
        DisconnectionReason Reason,
        bool IsRecoverable);

    public readonly record struct AudioFeedStats(
        long Accepted,
        long DroppedNotReady,
        long DroppedBackpressure,
        long DroppedClosed);

    public class SpeechRecognizeToken
    {
        public string Written { get; set; } = "";
        public double? Confidence { get; set; }
        public int? StartTime { get; set; }
        public int? EndTime { get; set; }
        public string? Spoken { get; set; }
    }

    public class SpeechRecogntionResult
    {
        public double? Confidence { get; set; }
        public int? StartTime { get; set; }
        public int? EndTime { get; set; }
        public string Text { get; set; } = "";
        public List<string>? Tags { get; set; }
        public string? Rulename { get; set; }
        public IList<SpeechRecognizeToken>? Tokens { get; set; }
    }

    public class SpeechRecognitionEventArgs
    {
        public IList<SpeechRecogntionResult>? Results { get; set; }
        public string? UtteranceId { get; set; }
        public string Text { get; set; } = "";
        public string? code { get; set; }
        public string? message { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? ExtensionData { get; set; }
    }

    public const string WaveFormatString = AmiVoiceProtocol.WaveFormatString;
    public const uint MaxReconnectCount = 5;

    public ProvidingStateType ProvidingState { get; private set; } = ProvidingStateType.Initialized;
    public DetectingStateType DetectingState { get; private set; } = DetectingStateType.NotDetecting;
    public RecognizingStateType RecognizingState { get; private set; } = RecognizingStateType.NotRecognizing;
    public string LastErrorString { get; private set; } = "";
    public Task? messageLoopTask { get; private set; }
    public StateSnapshot CurrentState { get; private set; } = new(
        ProvidingStateType.Initialized,
        DetectingStateType.NotDetecting,
        RecognizingStateType.NotRecognizing);
    public AudioFeedStats CurrentAudioFeedStats { get; private set; } = new(0, 0, 0, 0);

    public Uri ConnectionUri
    {
        get => connectionUri;
        set
        {
            if (!IsRunning)
            {
                connectionUri = value;
            }
        }
    }

    public string AppKey
    {
        get => appKey;
        set
        {
            if (!IsRunning)
            {
                appKey = value;
            }
        }
    }

    public string Engine
    {
        get => engine;
        set
        {
            if (!IsRunning)
            {
                engine = value;
            }
        }
    }

    public string? ProfileId
    {
        get => profileId;
        set
        {
            if (!IsRunning)
            {
                profileId = value;
            }
        }
    }

    public Dictionary<string, string> ConnectionParameter
    {
        get => connectionParameter;
        set
        {
            if (!IsRunning)
            {
                connectionParameter = value;
            }
        }
    }

    public event EventHandler<SpeechRecognitionEventArgs>? Recognized;
    public event EventHandler<SpeechRecognitionEventArgs>? Recognizing;
    public event EventHandler<uint>? VoiceStart;
    public event EventHandler<uint>? VoiceEnd;
    public event EventHandler<bool>? RecognizeStarting;
    public event EventHandler<string>? ErrorOccured;
    public event EventHandler<bool>? RecognizeStopped;
    public event EventHandler<string>? Trace;
    public event EventHandler<StateSnapshot>? StateChanged;
    public event EventHandler<DisconnectInfo>? Disconnected;
    public event EventHandler<AudioFeedStats>? AudioFeedStatsChanged;

    public VoiceRecognizerWithAmiVoiceCloud(in AmiVoiceAPI api)
    {
        var uri = new Uri(api.WebSocketURI.Trim());
        if (uri.Scheme != "ws" && uri.Scheme != "wss")
        {
            throw new ArgumentException("Invalid scheme");
        }

        connectionUri = uri;
        appKey = api.AppKey.Trim();
        engine = api.EngineName.Trim();
        profileId = api.ProfileId;
        connectionParameter = new Dictionary<string, string>();
    }

    public void Start(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }

        session = new AmiVoiceSession(
            connectionUri,
            appKey,
            engine,
            connectionParameter,
            jsonSerializerOptions);
        session.Recognized += (s, e) => Recognized?.Invoke(this, e);
        session.Recognizing += (s, e) => Recognizing?.Invoke(this, e);
        session.VoiceStart += (s, e) => VoiceStart?.Invoke(this, e);
        session.VoiceEnd += (s, e) => VoiceEnd?.Invoke(this, e);
        session.RecognizeStarting += (s, e) => RecognizeStarting?.Invoke(this, e);
        session.ErrorOccured += OnSessionError;
        session.RecognizeStopped += (s, e) => RecognizeStopped?.Invoke(this, e);
        session.Trace += (s, e) => Trace?.Invoke(this, e);
        session.ProvidingStateChanged += OnProvidingStateChanged;
        session.DetectingStateChanged += OnDetectingStateChanged;
        session.RecognizingStateChanged += OnRecognizingStateChanged;
        session.Disconnected += (s, e) => Disconnected?.Invoke(this, e);
        session.AudioFeedStatsChanged += OnAudioFeedStatsChanged;

        session.Start(cancellationToken);
        messageLoopTask = session.MessageLoopTask;
    }

    public Task StopAsync(TimeSpan timeout)
    {
        if (session is null)
        {
            return Task.CompletedTask;
        }
        return session.StopAsync(timeout);
    }

    public AudioFeedResult TryFeedRawWave(ReadOnlySpan<byte> rawWave)
    {
        if (session is null)
        {
            return AudioFeedResult.DroppedNotReady;
        }
        return session.TryFeedRawWave(rawWave);
    }

    private bool IsRunning => session is not null && session.MessageLoopTask is not null && !session.MessageLoopTask.IsCompleted;

    private void OnSessionError(object? sender, string error)
    {
        LastErrorString = error;
        ErrorOccured?.Invoke(this, error);
    }

    private void OnProvidingStateChanged(object? sender, ProvidingStateType state)
    {
        ProvidingState = state;
        PublishStateChanged();
    }

    private void OnDetectingStateChanged(object? sender, DetectingStateType state)
    {
        DetectingState = state;
        PublishStateChanged();
    }

    private void OnRecognizingStateChanged(object? sender, RecognizingStateType state)
    {
        RecognizingState = state;
        PublishStateChanged();
    }

    private void PublishStateChanged()
    {
        var snapshot = new StateSnapshot(ProvidingState, DetectingState, RecognizingState);
        if (CurrentState.Equals(snapshot))
        {
            return;
        }

        CurrentState = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    private void OnAudioFeedStatsChanged(object? sender, AudioFeedStats stats)
    {
        CurrentAudioFeedStats = stats;
        AudioFeedStatsChanged?.Invoke(this, stats);
    }
}
