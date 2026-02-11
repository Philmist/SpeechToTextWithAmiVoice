namespace SpeechToText.Core;

public interface IAudioCaptureServiceFactory
{
    IReadOnlyList<AudioInputDevice> GetAvailableDevices();
    IAudioCaptureService Create(AudioInputDevice device);
}
