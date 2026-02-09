using FluentAssertions;
using SpeechToText.Core.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace SpeechToText.Core.Tests;

public class VoiceRecognizerWithAmiVoiceCloudTests
{
    [Fact]
    public void Constructor_ShouldThrow_WhenWebSocketUriSchemeIsInvalid()
    {
        var api = CreateApi("http://127.0.0.1:18080/v1/");

        Action act = () => _ = new VoiceRecognizerWithAmiVoiceCloud(api);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryFeedRawWave_ShouldReturnDroppedNotReady_BeforeStart()
    {
        var recognizer = new VoiceRecognizerWithAmiVoiceCloud(CreateApi("ws://127.0.0.1:18080/v1/"));

        var result = recognizer.TryFeedRawWave(new byte[320]);

        result.Should().Be(VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedNotReady);
    }

    [Fact]
    public async Task Start_ShouldAuthenticate_AndCompleteOnCancellation()
    {
        await using var server = await FakeAmiVoiceWebSocketServer.StartAsync();
        var recognizer = new VoiceRecognizerWithAmiVoiceCloud(CreateApi(server.EndpointUri.ToString()));
        recognizer.ConnectionParameter["profileId"] = "profile with space";
        using var cancellationTokenSource = new CancellationTokenSource();
        var observedStates = new ConcurrentQueue<VoiceRecognizerWithAmiVoiceCloud.StateSnapshot>();
        recognizer.StateChanged += (_, state) =>
        {
            observedStates.Enqueue(state);
        };

        recognizer.Start(cancellationTokenSource.Token);

        var authPacket = await server.AuthPacketTask.WaitAsync(TimeSpan.FromSeconds(5));
        authPacket.Should().StartWith("s ");
        authPacket.Should().Contain("lsb16k -a-general");
        authPacket.Should().Contain("authorization=test-app-key");
        authPacket.Should().Contain("profileId=\"profile with space\"");

        await WaitUntilAsync(
            () => recognizer.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started ||
                  recognizer.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing,
            TimeSpan.FromSeconds(5));
        observedStates.Should().Contain(s => s.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Starting);
        observedStates.Should().Contain(s =>
            s.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Started ||
            s.ProvidingState == VoiceRecognizerWithAmiVoiceCloud.ProvidingStateType.Providing);

        var result = recognizer.TryFeedRawWave(new byte[320]);
        result.Should().BeOneOf(
            VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.Accepted,
            VoiceRecognizerWithAmiVoiceCloud.AudioFeedResult.DroppedBackpressure);

        cancellationTokenSource.Cancel();
        recognizer.messageLoopTask.Should().NotBeNull();
        _ = await Task.WhenAny(recognizer.messageLoopTask!, Task.Delay(TimeSpan.FromSeconds(5)));
        recognizer.messageLoopTask!.IsCompleted.Should().BeTrue();
    }

    private static AmiVoiceAPI CreateApi(string wsUri)
    {
        return new AmiVoiceAPI
        {
            WebSocketURI = wsUri,
            AppKey = "test-app-key",
            EngineName = "-a-general",
            ProfileId = "",
            FillerEnable = false
        };
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

    private sealed class FakeAmiVoiceWebSocketServer : IAsyncDisposable
    {
        private readonly HttpListener httpListener;
        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly Task acceptLoopTask;
        private WebSocket? webSocket;
        private readonly TaskCompletionSource<string> authPacketTaskSource;

        public Uri EndpointUri { get; }
        public Task<string> AuthPacketTask => authPacketTaskSource.Task;

        private FakeAmiVoiceWebSocketServer(Uri endpointUri, HttpListener httpListener)
        {
            EndpointUri = endpointUri;
            this.httpListener = httpListener;
            cancellationTokenSource = new CancellationTokenSource();
            authPacketTaskSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            acceptLoopTask = Task.Run(() => AcceptLoopAsync(cancellationTokenSource.Token));
        }

        public static Task<FakeAmiVoiceWebSocketServer> StartAsync()
        {
            var port = AcquireFreePort();
            var wsUri = new Uri($"ws://127.0.0.1:{port}/v1/");
            var httpUri = new Uri($"http://127.0.0.1:{port}/v1/");
            var listener = new HttpListener();
            listener.Prefixes.Add(httpUri.ToString());
            listener.Start();
            return Task.FromResult(new FakeAmiVoiceWebSocketServer(wsUri, listener));
        }

        public async ValueTask DisposeAsync()
        {
            cancellationTokenSource.Cancel();
            if (webSocket is not null)
            {
                try
                {
                    if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                    {
                        webSocket.Abort();
                    }
                }
                catch
                {
                }
                webSocket.Dispose();
            }

            httpListener.Stop();
            httpListener.Close();
            try
            {
                await acceptLoopTask;
            }
            catch
            {
            }
            cancellationTokenSource.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await httpListener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                HttpListenerWebSocketContext webSocketContext;
                try
                {
                    webSocketContext = await context.AcceptWebSocketAsync(null);
                }
                catch
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                    continue;
                }

                webSocket = webSocketContext.WebSocket;
                await HandleConnectionAsync(webSocket, cancellationToken);
            }
        }

        private async Task HandleConnectionAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var firstMessage = await ReceiveTextMessageAsync(socket, cancellationToken);
            authPacketTaskSource.TrySetResult(firstMessage);
            await SendTextMessageAsync(socket, "s", cancellationToken);

            var buffer = new byte[2048];
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                }
                catch
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }

        private static async Task<string> ReceiveTextMessageAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[2048];
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return "";
                }
                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static async Task SendTextMessageAsync(WebSocket socket, string text, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }

        private static int AcquireFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
