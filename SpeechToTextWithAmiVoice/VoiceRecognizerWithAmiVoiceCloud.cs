using System;
using System.Net.WebSockets;

namespace SpeechToTextWithAmiVoice
{
    class VoiceRecognizerWithAmiVoiceCloud
    {
        protected ClientWebSocket wsAmiVoice;
        public Uri ConnectionUri { get; protected set; }
        public string AppKey { get; protected set; }
    }
}
