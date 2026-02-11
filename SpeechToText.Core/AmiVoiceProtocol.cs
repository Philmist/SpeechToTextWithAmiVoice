#nullable enable

using System.Text;

namespace SpeechToText.Core;

internal enum AmiVoiceServerPacketType
{
    Unknown,
    Trace,
    Auth,
    Timeout,
    End,
    VoiceStart,
    VoiceEnd,
    RecognizeStart,
    Recognizing,
    Recognized
}

internal readonly record struct AmiVoiceServerPacket(
    AmiVoiceServerPacketType Type,
    string Raw,
    string Payload,
    bool IsError);

internal static class AmiVoiceProtocol
{
    internal const string WaveFormatString = "lsb16k";
    internal const byte PcmPrefixByte = 0x70;
    internal const string EndCommand = "e";

    public static string BuildAuthCommand(
        string engine,
        string appKey,
        IReadOnlyDictionary<string, string> additionalParameters)
    {
        var builder = new StringBuilder();
        builder.Append('s');
        builder.Append(' ');
        builder.Append(WaveFormatString);
        builder.Append(' ');
        builder.Append(engine);
        builder.Append(' ');
        builder.Append("authorization=");
        builder.Append(EscapeParameterValue(appKey));

        foreach (var pair in additionalParameters)
        {
            if (string.Equals(pair.Key, "authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Append(' ');
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(EscapeParameterValue(pair.Value));
        }
        return builder.ToString();
    }

    public static AmiVoiceServerPacket ParseServerPacket(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new AmiVoiceServerPacket(AmiVoiceServerPacketType.Unknown, raw, "", false);
        }

        var trimmed = raw.Trim(' ', '\0');
        var packetType = trimmed[0];
        var payload = GetPayload(trimmed);

        return packetType switch
        {
            'G' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.Trace, trimmed, payload, false),
            's' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.Auth, trimmed, payload, payload.Length > 0),
            'p' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.Timeout, trimmed, payload, payload.Length > 0),
            'e' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.End, trimmed, payload, payload.Length > 0),
            'S' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.VoiceStart, trimmed, payload, false),
            'E' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.VoiceEnd, trimmed, payload, false),
            'C' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.RecognizeStart, trimmed, payload, false),
            'U' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.Recognizing, trimmed, payload, false),
            'A' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.Recognized, trimmed, payload, false),
            'R' => new AmiVoiceServerPacket(AmiVoiceServerPacketType.Recognized, trimmed, payload, false),
            _ => new AmiVoiceServerPacket(AmiVoiceServerPacketType.Unknown, trimmed, payload, false)
        };
    }

    public static bool TryParseMilliseconds(string payload, out uint milliseconds)
    {
        return uint.TryParse(payload.Trim(), out milliseconds);
    }

    private static string EscapeParameterValue(string value)
    {
        if (value.IndexOf(' ') < 0)
        {
            return value;
        }
        return $"\"{value}\"";
    }

    private static string GetPayload(string packet)
    {
        if (packet.Length <= 1)
        {
            return "";
        }

        if (packet[1] == ' ')
        {
            return packet[2..].Trim();
        }

        return packet[1..].Trim();
    }
}
