using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Macross.Json.Extensions;

namespace SpeechToText.Core.Models
{
    public class PreferenceModel
    {
        public PrefStorageEnum PrefStorage { get; set; }
        public string? AppKey { get; set; }
        public string? RecognizedTextPostUrl { get; set; }
        [JsonConverter(typeof(JsonIPEndPointConverter))]
        public IPEndPoint? BouyomiChanAddress { get; set; }
        [JsonConverter(typeof(JsonStringEnumMemberConverter))]
        public BouyomiChanVoiceTypeEnum? BouyomiChanVoiceType { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<PrefStorageEnum>))]
    public enum PrefStorageEnum
    {
        Env,
        AppKey,
    }

    public enum BouyomiChanVoiceTypeEnum
    {
        Normal,
        Display,
        Female1,
        Female2,
        Male1,
        Male2,
        Neutral,
        Robot,
        Machine1,
        Machine2,
    }
}
