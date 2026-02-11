using FluentAssertions;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace SpeechToText.Core.Tests;

public class AmiVoiceSessionTests
{
    [Fact]
    public async Task TryFeedRawWave_ShouldReturnDroppedBackpressure_WhenSendLoopIsBlocked()
    {
        var transport = new FakeAmiVoiceTransport(blockBinarySend: true);
        transport.EnqueueText("s");
        var session = CreateSession(transport);
        using var cancellationTokenSource = new CancellationTokenSource();

        session.Start(cancellationTokenSource.Token);
        await WaitUntilAsync(
            () => session.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started ||
                  session.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing,
            TimeSpan.FromSeconds(5));

        var dropped = false;
        for (var i = 0; i < 256; i++)
        {
            var result = session.TryFeedRawWave(new byte[320]);
            if (result == VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedBackpressure)
            {
                dropped = true;
                break;
            }
        }

        dropped.Should().BeTrue();

        cancellationTokenSource.Cancel();
        transport.ReleaseBlockedBinarySend();
        session.MessageLoopTask.Should().NotBeNull();
        _ = await Task.WhenAny(session.MessageLoopTask!, Task.Delay(TimeSpan.FromSeconds(5)));
        session.MessageLoopTask!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task TryFeedRawWave_ShouldPublishAudioFeedStats_WhenBackpressureOccurs()
    {
        var transport = new FakeAmiVoiceTransport(blockBinarySend: true);
        transport.EnqueueText("s");
        var session = CreateSession(transport);
        using var cancellationTokenSource = new CancellationTokenSource();
        VoiceRecognizerWithAmiVoiceCloud.AudioFeedStats? observedStats = null;
        session.AudioFeedStatsChanged += (_, stats) => observedStats = stats;

        session.Start(cancellationTokenSource.Token);
        await WaitUntilAsync(
            () => session.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started ||
                  session.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing,
            TimeSpan.FromSeconds(5));

        for (var i = 0; i < 256; i++)
        {
            if (session.TryFeedRawWave(new byte[320]) == VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedBackpressure)
            {
                break;
            }
        }

        observedStats.Should().NotBeNull();
        observedStats!.Value.DroppedBackpressure.Should().BeGreaterThan(0);
        session.CurrentAudioFeedStats.DroppedBackpressure.Should().BeGreaterThan(0);

        cancellationTokenSource.Cancel();
        transport.ReleaseBlockedBinarySend();
        session.MessageLoopTask.Should().NotBeNull();
        _ = await Task.WhenAny(session.MessageLoopTask!, Task.Delay(TimeSpan.FromSeconds(5)));
        session.MessageLoopTask!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ReceiveLoop_ShouldRaiseRecognizingAndRecognizedEvents()
    {
        var transport = new FakeAmiVoiceTransport();
        transport.EnqueueText("s");
        transport.EnqueueText("C");
        transport.EnqueueText("U {\"text\":\"partial\"}");
        transport.EnqueueText("A {\"text\":\"final\"}");

        var session = CreateSession(transport);
        using var cancellationTokenSource = new CancellationTokenSource();
        var recognizingTaskSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recognizedTaskSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Recognizing += (_, e) => recognizingTaskSource.TrySetResult(e.Text);
        session.Recognized += (_, e) => recognizedTaskSource.TrySetResult(e.Text);

        session.Start(cancellationTokenSource.Token);

        var recognizing = await recognizingTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var recognized = await recognizedTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        recognizing.Should().Be("partial");
        recognized.Should().Be("final");
        session.RecognizingState.Should().Be(VoiceRecognizerWithAmiVoiceCloud.RecognizingStateType.NotRecognizing);

        cancellationTokenSource.Cancel();
        session.MessageLoopTask.Should().NotBeNull();
        _ = await Task.WhenAny(session.MessageLoopTask!, Task.Delay(TimeSpan.FromSeconds(5)));
        session.MessageLoopTask!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task StopByCancellation_ShouldPublishUserRequestedDisconnectReason()
    {
        var transport = new FakeAmiVoiceTransport();
        transport.EnqueueText("s");
        var session = CreateSession(transport);
        using var cancellationTokenSource = new CancellationTokenSource();
        VoiceRecognizerWithAmiVoiceCloud.DisconnectInfo? observedDisconnect = null;
        session.Disconnected += (_, info) => observedDisconnect = info;

        session.Start(cancellationTokenSource.Token);
        await WaitUntilAsync(
            () => session.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started ||
                  session.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing,
            TimeSpan.FromSeconds(5));

        cancellationTokenSource.Cancel();
        session.MessageLoopTask.Should().NotBeNull();
        _ = await Task.WhenAny(session.MessageLoopTask!, Task.Delay(TimeSpan.FromSeconds(5)));
        session.MessageLoopTask!.IsCompleted.Should().BeTrue();
        observedDisconnect.Should().NotBeNull();
        observedDisconnect!.Value.Reason.Should().Be(VoiceRecognizerWithAmiVoiceCloud.DisconnectionReason.UserRequestedStop);
        observedDisconnect!.Value.IsRecoverable.Should().BeFalse();
    }

    private static AmiVoiceSession CreateSession(FakeAmiVoiceTransport transport)
    {
        return new AmiVoiceSession(
            new Uri("ws://127.0.0.1:18080/v1/"),
            "app-key",
            "-a-general",
            new Dictionary<string, string>(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            () => transport);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition was not satisfied within timeout.");
            }
            await Task.Delay(20);
        }
    }

    private sealed class FakeAmiVoiceTransport : IAmiVoiceWebSocketTransport
    {
        private readonly Channel<ReceiveFrame> receiveFrames = Channel.CreateUnbounded<ReceiveFrame>();
        private readonly TaskCompletionSource<bool> binarySendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool blockBinarySend;
        private WebSocketState state = WebSocketState.None;

        public FakeAmiVoiceTransport(bool blockBinarySend = false)
        {
            this.blockBinarySend = blockBinarySend;
        }

        public WebSocketState State => state;

        public Task ConnectAsync(Uri connectionUri, CancellationToken cancellationToken)
        {
            state = WebSocketState.Open;
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task SendBinaryAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            if (!blockBinarySend)
            {
                return;
            }

            await binarySendGate.Task.WaitAsync(cancellationToken);
        }

        public async Task<AmiVoiceReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            var frame = await receiveFrames.Reader.ReadAsync(cancellationToken);
            if (frame.MessageType == WebSocketMessageType.Close)
            {
                state = WebSocketState.CloseReceived;
                return new AmiVoiceReceiveResult(
                    0,
                    true,
                    WebSocketMessageType.Close,
                    WebSocketCloseStatus.NormalClosure,
                    "");
            }

            var bytes = Encoding.UTF8.GetBytes(frame.Text);
            Array.Copy(bytes, buffer, bytes.Length);
            return new AmiVoiceReceiveResult(
                bytes.Length,
                true,
                WebSocketMessageType.Text,
                null,
                null);
        }

        public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public void Abort()
        {
            state = WebSocketState.Aborted;
        }

        public ValueTask DisposeAsync()
        {
            receiveFrames.Writer.TryComplete();
            binarySendGate.TrySetCanceled();
            return ValueTask.CompletedTask;
        }

        public void EnqueueText(string text)
        {
            receiveFrames.Writer.TryWrite(new ReceiveFrame(WebSocketMessageType.Text, text));
        }

        public void ReleaseBlockedBinarySend()
        {
            binarySendGate.TrySetResult(true);
        }

        private readonly record struct ReceiveFrame(WebSocketMessageType MessageType, string Text);
    }
}
