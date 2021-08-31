using System;
using System.Collections.Generic;

namespace SpeechToTextWithAmiVoice.Models
{
    class SpeechToTextSettings
    {
        public string OutputTextfilePath { get; set; }
        public bool OutputClearingIsEnabled { get; set; }
        public double OutputClearingSeconds { get; set; }
        public string BouyomiChanUri { get; set; } = "127.0.0.1";
        public int BouyomiChanPort { get; set; } = 50001;
        public bool BouyomiChanIsEnabled { get; set; }
        public string BouyomiChanPrefix { get; set; } = "";

        public class BouyomiChanVoiceMapper
        {
            public string Name { get; set; }
            public Int16 Tone { get; set; }
        }
        public readonly List<BouyomiChanVoiceMapper> BouyomiChanVoiceMap = new List<BouyomiChanVoiceMapper> {
            new BouyomiChanVoiceMapper { Name = "Normal", Tone = -1 },
            new BouyomiChanVoiceMapper { Name = "Display", Tone = 0 },
            new BouyomiChanVoiceMapper { Name = "Female1", Tone = 1 },
            new BouyomiChanVoiceMapper { Name = "Female2", Tone = 2 },
            new BouyomiChanVoiceMapper { Name = "Male1", Tone = 3 },
            new BouyomiChanVoiceMapper { Name = "Male2", Tone = 4 },
            new BouyomiChanVoiceMapper { Name = "Neutral", Tone = 5 },
            new BouyomiChanVoiceMapper { Name = "Robot", Tone = 6 },
            new BouyomiChanVoiceMapper { Name = "Machine1", Tone = 7 },
            new BouyomiChanVoiceMapper { Name = "Machine2", Tone = 8 }
        };

        public SpeechToTextSettings() { }
        public SpeechToTextSettings(SpeechToTextSettings pObj)
        {
            OutputTextfilePath = pObj.OutputTextfilePath;
            OutputClearingIsEnabled = pObj.OutputClearingIsEnabled;
            OutputClearingSeconds = pObj.OutputClearingSeconds;
            BouyomiChanUri = pObj.BouyomiChanUri;
            BouyomiChanPort = pObj.BouyomiChanPort;
            BouyomiChanIsEnabled = pObj.BouyomiChanIsEnabled;
        }
    }
}
