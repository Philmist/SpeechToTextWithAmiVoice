using NAudio.CoreAudioApi;
using NAudio.Wave;
using SpeechToText.Core;

namespace SpeechToTextAmiVoiceMAUI.Platforms.Windows;

public sealed class WasapiMauiAudioCaptureServiceFactory : IAudioCaptureServiceFactory
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
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(device.Id))
        {
            throw new ArgumentException("Audio device id is required.", nameof(device));
        }

        using var deviceEnum = new MMDeviceEnumerator();
        var mmDevice = deviceEnum.GetDevice(device.Id);
        return new WasapiMauiAudioCaptureService(mmDevice);
    }
}

internal sealed class WasapiMauiAudioCaptureService : IAudioCaptureService
{
    private readonly WasapiCapture capture;
    private readonly AudioMeterInformation audioMeterInformation;
    private readonly IObservable<ReadOnlyMemory<byte>> pcm16StreamObservable;

    public event EventHandler<ReadOnlyMemory<byte>>? ResampledDataAvailable;
    public event EventHandler<float>? ResampledMaxValueAvailable;

    public IObservable<ReadOnlyMemory<byte>> Pcm16StreamObservable => pcm16StreamObservable;

    public WasapiMauiAudioCaptureService(MMDevice device)
    {
        if (device.DataFlow != DataFlow.Capture && device.DataFlow != DataFlow.All)
        {
            throw new ArgumentException("Device does not have capture capability.", nameof(device));
        }

        capture = new WasapiCapture(device)
        {
            ShareMode = AudioClientShareMode.Shared,
            WaveFormat = new WaveFormat(16000, 16, 1)
        };
        capture.DataAvailable += OnDataAvailable;
        audioMeterInformation = device.AudioMeterInformation;
        pcm16StreamObservable = new EventObservable<ReadOnlyMemory<byte>>(
            h => ResampledDataAvailable += h,
            h => ResampledDataAvailable -= h);
    }

    public void StartRecording()
    {
        capture.StartRecording();
    }

    public void StopRecording()
    {
        capture.StopRecording();
    }

    public void Dispose()
    {
        capture.DataAvailable -= OnDataAvailable;
        capture.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        var data = new ReadOnlyMemory<byte>(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
        ResampledDataAvailable?.Invoke(this, data);
        ResampledMaxValueAvailable?.Invoke(this, audioMeterInformation.MasterPeakValue);
    }

    private sealed class EventObservable<T> : IObservable<T>
    {
        private readonly Action<EventHandler<T>> subscribe;
        private readonly Action<EventHandler<T>> unsubscribe;

        public EventObservable(Action<EventHandler<T>> subscribe, Action<EventHandler<T>> unsubscribe)
        {
            this.subscribe = subscribe;
            this.unsubscribe = unsubscribe;
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            EventHandler<T>? handler = null;
            handler = (_, value) => observer.OnNext(value);
            subscribe(handler);
            return new Subscription(() =>
            {
                if (handler is not null)
                {
                    unsubscribe(handler);
                }
            });
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action disposeAction;
            private bool disposed;

            public Subscription(Action disposeAction)
            {
                this.disposeAction = disposeAction;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                disposeAction();
                disposed = true;
            }
        }
    }
}
