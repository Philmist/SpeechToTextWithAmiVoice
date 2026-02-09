using SpeechToText.Core;

namespace SpeechToTextAmiVoiceMAUI;

public sealed class UnsupportedAudioCaptureServiceFactory : IAudioCaptureServiceFactory
{
    public IReadOnlyList<AudioInputDevice> GetAvailableDevices()
    {
        return Array.Empty<AudioInputDevice>();
    }

    public IAudioCaptureService Create(AudioInputDevice device)
    {
        throw new PlatformNotSupportedException("Audio capture is not implemented on this platform yet.");
    }
}
