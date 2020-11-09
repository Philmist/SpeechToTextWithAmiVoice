namespace SpeechToTextWithAmiVoice.Models
{
    class SpeechToTextSettings
    {
        public string OutputTextfilePath { get; set; }
        public bool OutputClearingIsEnabled { get; set; }
        public double OutputClearingSeconds { get; set; }
        public string BouyomiChanUri { get; set; }
        public int BouyomiChanPort { get; set; }
        public bool BouyomiChanIsEnabled { get; set; }
    }
}
