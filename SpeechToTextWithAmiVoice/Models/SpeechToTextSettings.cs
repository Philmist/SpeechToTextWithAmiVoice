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
