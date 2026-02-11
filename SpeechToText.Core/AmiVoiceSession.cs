#nullable enable

using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace SpeechToText.Core;

internal sealed class AmiVoiceSession
{
    private const int PcmChannelCapacity = 32;
    private const int ReceiveBufferLength = 8192;
    private readonly Uri connectionUri;
    private readonly string appKey;
    private readonly string engine;
    private readonly Dictionary<string, string> connectionParameter;
    private readonly JsonSerializerOptions serializerOptions;
    private readonly Func<IAmiVoiceWebSocketTransport> transportFactory;
    private readonly Channel<PcmChunk> pcmChannel;
    private readonly object startLock = new();
    private readonly struct PcmChunk
    {
        public PcmChunk(byte[] buffer, int length)
        {
            Buffer = buffer;
            Length = length;
        }

        public byte[] Buffer { get; }
        public int Length { get; }
    }

    private CancellationTokenSource? linkedCancellationTokenSource;
    private Task? messageLoopTask;
    private volatile bool transportReady;
    private volatile bool isStopping;
    private long acceptedAudioFeedCount;
    private long droppedNotReadyAudioFeedCount;
    private long droppedBackpressureAudioFeedCount;
    private long droppedClosedAudioFeedCount;

    public VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType ProvidingState { get; private set; } = VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Initialized;
    public VoiceRecognizerWithAmiVoiceCloud.DetectingStateType DetectingState { get; private set; } = VoiceRecognizerWithAmiVoiceCloud.DetectingStateType.NotDetecting;
    public VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType RecognizingState { get; private set; } = VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType.NotRecognizing;
    public string LastErrorString { get; private set; } = "";
    public Task? MessageLoopTask => messageLoopTask;
    public VoiceRecognizerWithAmiVoiceCloud.AudioFeedStats CurrentAudioFeedStats { get; private set; } = new(0, 0, 0, 0);

    public event EventHandler<VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs>? Recognizing;
    public event EventHandler<VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs>? Recognized;
    public event EventHandler<uint>? VoiceStart;
    public event EventHandler<uint>? VoiceEnd;
    public event EventHandler<bool>? RecognizeStarting;
    public event EventHandler<string>? ErrorOccured;
    public event EventHandler<bool>? RecognizeStopped;
    public event EventHandler<string>? Trace;
    public event EventHandler<VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType>? ProvidingStateChanged;
    public event EventHandler<VoiceRecognizerWithAmiVoiceCloud.DetectingStateType>? DetectingStateChanged;
    public event EventHandler<VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType>? RecognizingStateChanged;
    public event EventHandler<VoiceRecognizerWithAmiVoiceCloud.DisconnectInfo>? Disconnected;
    public event EventHandler<VoiceRecognizerWithAmiVoiceCloud.AudioFeedStats>? AudioFeedStatsChanged;

    public AmiVoiceSession(
        Uri connectionUri,
        string appKey,
        string engine,
        IReadOnlyDictionary<string, string> connectionParameter,
        JsonSerializerOptions serializerOptions,
        Func<IAmiVoiceWebSocketTransport>? transportFactory = null)
    {
        this.connectionUri = connectionUri;
        this.appKey = appKey;
        this.engine = engine;
        this.connectionParameter = new Dictionary<string, string>(connectionParameter);
        this.serializerOptions = serializerOptions;
        this.transportFactory = transportFactory ?? (() => new ClientWebSocketTransport());
        pcmChannel = Channel.CreateBounded<PcmChunk>(new BoundedChannelOptions(PcmChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Start(CancellationToken cancellationToken)
    {
        lock (startLock)
        {
            if (messageLoopTask is not null && !messageLoopTask.IsCompleted)
            {
                return;
            }

            linkedCancellationTokenSource?.Dispose();
            linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var linkedToken = linkedCancellationTokenSource.Token;
            messageLoopTask = Task.Run(async () => await MessageLoopAsync(linkedToken), linkedToken);
        }
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        linkedCancellationTokenSource?.Cancel();
        var task = messageLoopTask;
        if (task is null)
        {
            return;
        }

        var completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (completedTask != task)
        {
            Trace?.Invoke(this, "StopAsync timeout.");
        }
    }

    public VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult TryFeedRawWave(ReadOnlySpan<byte> rawWave)
    {
        if (ProvidingState != VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started &&
            ProvidingState != VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing)
        {
            return RecordAudioFeedResult(VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedNotReady);
        }

        if (!transportReady)
        {
            return RecordAudioFeedResult(VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedClosed);
        }

        if (rawWave.Length == 0)
        {
            return RecordAudioFeedResult(VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedNotReady);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(rawWave.Length);
        rawWave.CopyTo(buffer.AsSpan(0, rawWave.Length));
        var chunk = new PcmChunk(buffer, rawWave.Length);
        if (!pcmChannel.Writer.TryWrite(chunk))
        {
            ArrayPool<byte>.Shared.Return(buffer);
            return RecordAudioFeedResult(VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedBackpressure);
        }

        return RecordAudioFeedResult(VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.Accepted);
    }

    private async Task MessageLoopAsync(CancellationToken cancellationToken)
    {
        SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Initialized);
        SetDetectingState(VoiceRecognizerWithAmiVoiceCloud.DetectingStateType.NotDetecting);
        SetRecognizingState(VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType.NotRecognizing);

        var reconnectCount = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var transport = transportFactory();
                transportReady = false;
                isStopping = false;

                try
                {
                    Trace?.Invoke(this, "Try to connect.");
                    SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Starting);
                    await transport.ConnectAsync(connectionUri, cancellationToken);

                    var authCommand = AmiVoiceProtocol.BuildAuthCommand(engine, appKey, connectionParameter);
                    await transport.SendTextAsync(authCommand, cancellationToken);
                    await WaitForAuthResultAsync(transport, cancellationToken);

                    reconnectCount = 0;
                    transportReady = true;
                    SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started);
                    Trace?.Invoke(this, "Connection complete.");

                    using var loopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var loopCancellationToken = loopCancellationTokenSource.Token;
                    var receiveTask = Task.Run(async () => await ReceiveLoopAsync(transport, loopCancellationToken), loopCancellationToken);
                    var sendTask = Task.Run(async () => await SendLoopAsync(transport, loopCancellationToken), loopCancellationToken);
                    var completedTask = await Task.WhenAny(receiveTask, sendTask);

                    loopCancellationTokenSource.Cancel();
                    await AwaitTaskIgnoreCancellationAsync(receiveTask);
                    await AwaitTaskIgnoreCancellationAsync(sendTask);

                    if (completedTask.Exception is not null)
                    {
                        throw completedTask.Exception.InnerException ?? completedTask.Exception;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        await StopTransportAsync(transport);
                        break;
                    }

                    reconnectCount++;
                    PublishDisconnected(VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerClosed, isRecoverable: true);
                    if (reconnectCount > VoiceRecognizerWithAmiVoiceCloud.MaxReconnectCount)
                    {
                        SetFatalError("Connection closed.");
                        PublishDisconnected(VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ReconnectLimitReached, isRecoverable: false);
                        break;
                    }

                    SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Initialized);
                    SetDetectingState(VoiceRecognizerWithAmiVoiceCloud.DetectingStateType.NotDetecting);
                    SetRecognizingState(VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType.NotRecognizing);
                    Trace?.Invoke(this, $"Disconnected. Reconnecting {reconnectCount}/{VoiceRecognizerWithAmiVoiceCloud.MaxReconnectCount}.");
                    ReturnPendingPcmBuffers();
                    await Task.Delay(ComputeReconnectDelay(reconnectCount), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await StopTransportAsync(transport);
                    break;
                }
                catch (AmiVoiceSessionFatalException ex)
                {
                    SetFatalError(ex.Message);
                    PublishDisconnected(ex.Reason, isRecoverable: false);
                    break;
                }
                catch (Exception ex) when (ex is WebSocketException || ex is AmiVoiceSessionReconnectException || ex is IOException)
                {
                    var reason = ResolveReconnectReason(ex);
                    reconnectCount++;
                    PublishDisconnected(reason, isRecoverable: true);
                    if (reconnectCount > VoiceRecognizerWithAmiVoiceCloud.MaxReconnectCount)
                    {
                        SetFatalError(ex.Message);
                        PublishDisconnected(VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ReconnectLimitReached, isRecoverable: false);
                        break;
                    }

                    SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Initialized);
                    SetDetectingState(VoiceRecognizerWithAmiVoiceCloud.DetectingStateType.NotDetecting);
                    SetRecognizingState(VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType.NotRecognizing);
                    Trace?.Invoke(this, $"Connection error: {ex.Message}");
                    Trace?.Invoke(this, $"Reconnect {reconnectCount}/{VoiceRecognizerWithAmiVoiceCloud.MaxReconnectCount}.");
                    ReturnPendingPcmBuffers();
                    await Task.Delay(ComputeReconnectDelay(reconnectCount), cancellationToken);
                }
                finally
                {
                    transportReady = false;
                }
            }
        }
        finally
        {
            ReturnPendingPcmBuffers();
            SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Initialized);
            SetDetectingState(VoiceRecognizerWithAmiVoiceCloud.DetectingStateType.NotDetecting);
            SetRecognizingState(VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType.NotRecognizing);
            Trace?.Invoke(this, "Disconnected.");
            if (cancellationToken.IsCancellationRequested)
            {
                PublishDisconnected(VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.UserRequestedStop, isRecoverable: false);
            }
            RecognizeStopped?.Invoke(this, true);
        }
    }

    private static TimeSpan ComputeReconnectDelay(int reconnectCount)
    {
        var seconds = Math.Min(Math.Pow(2, reconnectCount - 1) * 0.5, 8.0);
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task WaitForAuthResultAsync(IAmiVoiceWebSocketTransport transport, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var raw = await ReceiveTextAsync(transport, cancellationToken);
            if (raw is null)
            {
                throw new AmiVoiceSessionReconnectException(
                    "Auth response was not received.",
                    VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerClosed);
            }

            var packet = AmiVoiceProtocol.ParseServerPacket(raw);
            switch (packet.Type)
            {
                case AmiVoiceServerPacketType.Trace:
                    if (!string.IsNullOrWhiteSpace(packet.Payload))
                    {
                        Trace?.Invoke(this, packet.Payload);
                    }
                    break;
                case AmiVoiceServerPacketType.Auth:
                    if (packet.IsError)
                    {
                        throw new AmiVoiceSessionFatalException(
                            packet.Payload,
                            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.AuthFailed);
                    }
                    return;
                default:
                    throw new AmiVoiceSessionReconnectException(
                        $"Unexpected auth response: {packet.Raw}",
                        VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.UnexpectedProtocol);
            }
        }
    }

    private async Task SendLoopAsync(IAmiVoiceWebSocketTransport transport, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var chunk = await pcmChannel.Reader.ReadAsync(cancellationToken);
            var sendBuffer = ArrayPool<byte>.Shared.Rent(chunk.Length + 1);
            sendBuffer[0] = AmiVoiceProtocol.PcmPrefixByte;
            Buffer.BlockCopy(chunk.Buffer, 0, sendBuffer, 1, chunk.Length);

            try
            {
                if (transport.State != WebSocketState.Open)
                {
                    throw new AmiVoiceSessionReconnectException(
                        "WebSocket is not open.",
                        VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerClosed);
                }

                await transport.SendBinaryAsync(
                    sendBuffer.AsMemory(0, chunk.Length + 1),
                    cancellationToken);
                if (ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started)
                {
                    SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sendBuffer);
                ArrayPool<byte>.Shared.Return(chunk.Buffer);
            }
        }
    }

    private async Task ReceiveLoopAsync(IAmiVoiceWebSocketTransport transport, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var raw = await ReceiveTextAsync(transport, cancellationToken);
            if (raw is null)
            {
                throw new AmiVoiceSessionReconnectException(
                    "WebSocket closed by server.",
                    VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerClosed);
            }

            var packet = AmiVoiceProtocol.ParseServerPacket(raw);
            switch (packet.Type)
            {
                case AmiVoiceServerPacketType.Trace:
                    if (!string.IsNullOrWhiteSpace(packet.Payload))
                    {
                        Trace?.Invoke(this, packet.Payload);
                    }
                    break;
                case AmiVoiceServerPacketType.Timeout:
                    if (packet.IsError)
                    {
                        throw new AmiVoiceSessionReconnectException(
                            packet.Payload,
                            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerTimeout);
                    }
                    break;
                case AmiVoiceServerPacketType.End:
                    if (packet.IsError)
                    {
                        throw new AmiVoiceSessionReconnectException(
                            packet.Payload,
                            VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerEndedSession);
                    }
                    if (isStopping || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    throw new AmiVoiceSessionReconnectException(
                        "Server ended the session.",
                        VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.ServerEndedSession);
                case AmiVoiceServerPacketType.VoiceStart:
                    if (AmiVoiceProtocol.TryParseMilliseconds(packet.Payload, out var startMs))
                    {
                        VoiceStart?.Invoke(this, startMs);
                    }
                    SetDetectingState(VoiceRecognizerWithAmiVoiceCloud.DetectingStateType.Detecting);
                    break;
                case AmiVoiceServerPacketType.VoiceEnd:
                    if (AmiVoiceProtocol.TryParseMilliseconds(packet.Payload, out var endMs))
                    {
                        VoiceEnd?.Invoke(this, endMs);
                    }
                    SetDetectingState(VoiceRecognizerWithAmiVoiceCloud.DetectingStateType.NotDetecting);
                    break;
                case AmiVoiceServerPacketType.RecognizeStart:
                    SetRecognizingState(VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType.Recognizing);
                    RecognizeStarting?.Invoke(this, true);
                    break;
                case AmiVoiceServerPacketType.Recognizing:
                    PublishRecognitionEvent(packet.Payload, isFinal: false);
                    break;
                case AmiVoiceServerPacketType.Recognized:
                    PublishRecognitionEvent(packet.Payload, isFinal: true);
                    SetRecognizingState(VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType.NotRecognizing);
                    break;
                case AmiVoiceServerPacketType.Auth:
                case AmiVoiceServerPacketType.Unknown:
                default:
                    break;
            }
        }
    }

    private void PublishRecognitionEvent(string payload, bool isFinal)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            var result = JsonSerializer.Deserialize<VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs>(payload, serializerOptions);
            if (result is null)
            {
                return;
            }

            if (isFinal)
            {
                Recognized?.Invoke(this, result);
            }
            else
            {
                Recognizing?.Invoke(this, result);
            }
        }
        catch (JsonException ex)
        {
            Trace?.Invoke(this, $"JSON parse error: {ex.Message}");
        }
    }

    private async Task<string?> ReceiveTextAsync(IAmiVoiceWebSocketTransport transport, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferLength];
        using var memoryStream = new MemoryStream();
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await transport.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                memoryStream.Write(buffer, 0, result.Count);
            }

            if (!result.EndOfMessage)
            {
                continue;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                return "";
            }

            return Encoding.UTF8.GetString(memoryStream.ToArray());
        }

        return null;
    }

    private async Task StopTransportAsync(IAmiVoiceWebSocketTransport transport)
    {
        isStopping = true;
        SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Ending);

        try
        {
            if (transport.State == WebSocketState.Open)
            {
                await transport.SendTextAsync(AmiVoiceProtocol.EndCommand, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is WebSocketException || ex is IOException)
        {
            Trace?.Invoke(this, $"Stop send error: {ex.Message}");
        }

        try
        {
            await transport.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException || ex is IOException)
        {
            Trace?.Invoke(this, $"Close output error: {ex.Message}");
        }

        try
        {
            await transport.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException || ex is IOException)
        {
            Trace?.Invoke(this, $"Close error: {ex.Message}");
        }
    }

    private void ReturnPendingPcmBuffers()
    {
        while (pcmChannel.Reader.TryRead(out var chunk))
        {
            ArrayPool<byte>.Shared.Return(chunk.Buffer);
        }
    }

    private VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult RecordAudioFeedResult(VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult result)
    {
        switch (result)
        {
            case VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.Accepted:
                Interlocked.Increment(ref acceptedAudioFeedCount);
                break;
            case VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedNotReady:
                Interlocked.Increment(ref droppedNotReadyAudioFeedCount);
                break;
            case VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedBackpressure:
                Interlocked.Increment(ref droppedBackpressureAudioFeedCount);
                break;
            case VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedClosed:
                Interlocked.Increment(ref droppedClosedAudioFeedCount);
                break;
        }

        var snapshot = new VoiceRecognizerWithAmiVoiceCloud.AudioFeedStats(
            Interlocked.Read(ref acceptedAudioFeedCount),
            Interlocked.Read(ref droppedNotReadyAudioFeedCount),
            Interlocked.Read(ref droppedBackpressureAudioFeedCount),
            Interlocked.Read(ref droppedClosedAudioFeedCount));
        CurrentAudioFeedStats = snapshot;

        if (result != VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.Accepted)
        {
            AudioFeedStatsChanged?.Invoke(this, snapshot);
        }

        return result;
    }

    private void PublishDisconnected(VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason reason, bool isRecoverable)
    {
        Disconnected?.Invoke(this, new VoiceRecognizerWithAmiVoiceCloud.DisconnectInfo(reason, isRecoverable));
    }

    private static VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason ResolveReconnectReason(Exception ex)
    {
        if (ex is AmiVoiceSessionReconnectException reconnectException)
        {
            return reconnectException.Reason;
        }

        return VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.TransportError;
    }

    private void SetFatalError(string error)
    {
        LastErrorString = error;
        SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Error);
        SetDetectingState(VoiceRecognizerWithAmiVoiceCloud.DetectingStateType.NotDetecting);
        SetRecognizingState(VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType.NotRecognizing);
        ErrorOccured?.Invoke(this, error);
    }

    private void SetProvidingState(VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType state)
    {
        if (ProvidingState == state)
        {
            return;
        }
        ProvidingState = state;
        ProvidingStateChanged?.Invoke(this, state);
    }

    private void SetDetectingState(VoiceRecognizerWithAmiVoiceCloud.DetectingStateType state)
    {
        if (DetectingState == state)
        {
            return;
        }
        DetectingState = state;
        DetectingStateChanged?.Invoke(this, state);
    }

    private void SetRecognizingState(VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType state)
    {
        if (RecognizingState == state)
        {
            return;
        }
        RecognizingState = state;
        RecognizingStateChanged?.Invoke(this, state);
    }

    private static async Task AwaitTaskIgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class AmiVoiceSessionFatalException : Exception
    {
        public AmiVoiceSessionFatalException(string message, VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason reason) : base(message)
        {
            Reason = reason;
        }

        public VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason Reason { get; }
    }

    private sealed class AmiVoiceSessionReconnectException : Exception
    {
        public AmiVoiceSessionReconnectException(string message, VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason reason) : base(message)
        {
            Reason = reason;
        }

        public VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason Reason { get; }
    }
}
