using NAudio.CoreAudioApi;
using SpeechToText.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpeechToTextWithAmiVoice;

sealed class WasapiAudioCaptureServiceFactory : IAudioCaptureServiceFactory
{
    public IReadOnlyList<AudioInputDevice> GetAvailableDevices()
    {
        using var deviceEnum = new MMDeviceEnumerator();
        return deviceEnum
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioInputDevice
            {
                Id = d.ID,
                FriendlyName = d.FriendlyName
            })
            .ToList();
    }

    public IAudioCaptureService Create(AudioInputDevice device)
    {
        if (device is null)
        {
            throw new ArgumentNullException(nameof(device));
        }
        if (string.IsNullOrWhiteSpace(device.Id))
        {
            throw new ArgumentException("Audio device id is required.", nameof(device));
        }

        using var deviceEnum = new MMDeviceEnumerator();
        var mmDevice = deviceEnum.GetDevice(device.Id);
        return new CaptureVoiceFromWasapi(mmDevice);
    }
}
