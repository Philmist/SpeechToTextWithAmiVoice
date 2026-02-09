#nullable enable

using System.Net.WebSockets;

namespace SpeechToText.Core;

internal readonly record struct AmiVoiceReceiveResult(
    int Count,
    bool EndOfMessage,
    WebSocketMessageType MessageType,
    WebSocketCloseStatus? CloseStatus,
    string? CloseStatusDescription);

internal interface IAmiVoiceWebSocketTransport : IAsyncDisposable
{
    WebSocketState State { get; }
    Task ConnectAsync(Uri connectionUri, CancellationToken cancellationToken);
    Task SendTextAsync(string text, CancellationToken cancellationToken);
    Task SendBinaryAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    Task<AmiVoiceReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken);
    Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken);
    Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken);
    void Abort();
}

internal sealed class ClientWebSocketTransport : IAmiVoiceWebSocketTransport
{
    private readonly ClientWebSocket clientWebSocket;

    public ClientWebSocketTransport()
    {
        clientWebSocket = new ClientWebSocket();
        // AmiVoice may drop the session when websocket ping frames are sent.
        clientWebSocket.Options.KeepAliveInterval = TimeSpan.Zero;
    }

    public WebSocketState State => clientWebSocket.State;

    public Task ConnectAsync(Uri connectionUri, CancellationToken cancellationToken)
    {
        return clientWebSocket.ConnectAsync(connectionUri, cancellationToken);
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(text);
        return clientWebSocket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }

    public Task SendBinaryAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        return clientWebSocket.SendAsync(payload, WebSocketMessageType.Binary, true, cancellationToken).AsTask();
    }

    public async Task<AmiVoiceReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var result = await clientWebSocket.ReceiveAsync(buffer, cancellationToken);
        return new AmiVoiceReceiveResult(
            result.Count,
            result.EndOfMessage,
            result.MessageType,
            result.CloseStatus,
            result.CloseStatusDescription);
    }

    public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
    {
        if (clientWebSocket.State == WebSocketState.Open || clientWebSocket.State == WebSocketState.CloseReceived)
        {
            return clientWebSocket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
        }
        return Task.CompletedTask;
    }

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
    {
        if (clientWebSocket.State == WebSocketState.Open || clientWebSocket.State == WebSocketState.CloseReceived)
        {
            return clientWebSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
        }
        return Task.CompletedTask;
    }

    public void Abort()
    {
        clientWebSocket.Abort();
    }

    public ValueTask DisposeAsync()
    {
        clientWebSocket.Dispose();
        return ValueTask.CompletedTask;
    }
}
