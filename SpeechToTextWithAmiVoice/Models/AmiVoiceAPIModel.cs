namespace SpeechToTextWithAmiVoice.Models
{
    //using AmiVoiceEngineItem = (string FriendlyName, string ConnectionId);
    public struct AmiVoiceEngineItem
    {
        public string FriendlyName { get; init; }
        public string ConnectionId { get; init; }
    }
    class AmiVoiceAPI
    {
        public string WebSocketURI { get; set; }
        public string AppKey { get; set; }
        public string ProfileId { get; set; }
        public string EngineName { get; set; }
        public bool FillerEnable { get; set; }
        public static readonly AmiVoiceEngineItem[] PreDefinedEngines = [
            new AmiVoiceEngineItem{ FriendlyName = "日本語 E2E 汎用", ConnectionId = "-a2-ja-general" },
            new AmiVoiceEngineItem{ FriendlyName ="日本語 Hybrid 汎用", ConnectionId ="-a-general" },
            new AmiVoiceEngineItem{ FriendlyName = "日本語 Hybrid 音声入力", ConnectionId = "-a-general-input" },
            new AmiVoiceEngineItem{ FriendlyName = "多言語 E2E 汎用", ConnectionId = "-a2-multi-general" },
            new AmiVoiceEngineItem{ FriendlyName = "英語 Hybrid 汎用", ConnectionId = "-a-general-en" },
            new AmiVoiceEngineItem{ FriendlyName = "中国語 E2E 汎用", ConnectionId = "-a2-zh-general" },
            new AmiVoiceEngineItem{ FriendlyName = "中国語 Hybrid 汎用", ConnectionId = "-a-general-zh" },
            new AmiVoiceEngineItem{ FriendlyName = "韓国語 Hybrid 汎用", ConnectionId = "-a-general-ko" },
        ];
    }
}
