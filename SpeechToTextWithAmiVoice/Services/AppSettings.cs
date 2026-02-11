namespace SpeechToTextWithAmiVoice.Services;

public sealed class ConnectionSettings
{
    public const string DefaultWebSocketUri = "wss://acp-api.amivoice.com/v1/";

    public string WebSocketUri { get; set; } = DefaultWebSocketUri;
    public string ApiKey { get; set; } = "";
    public string HttpPostUri { get; set; } = "";
    public string BouyomiHost { get; set; } = "127.0.0.1";
    public int BouyomiPort { get; set; } = 50001;
    public string BouyomiPrefix { get; set; } = "";
    public short BouyomiVoiceTone { get; set; } = -1;
}

public sealed class RuntimeOptions
{
    public string ProfileId { get; set; } = "";
    public bool FillerEnabled { get; set; }
    public string EngineConnectionId { get; set; } = "-a-general";
    public string AudioDeviceId { get; set; } = "";
    public bool EnableHttpPost { get; set; }
    public bool EnableBouyomi { get; set; }
}
