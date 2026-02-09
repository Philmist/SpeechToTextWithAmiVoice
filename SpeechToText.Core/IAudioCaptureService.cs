namespace SpeechToText.Core;

public interface IAudioCaptureService : IDisposable
{
    event EventHandler<ReadOnlyMemory<byte>> ResampledDataAvailable;
    event EventHandler<float> ResampledMaxValueAvailable;

    IObservable<ReadOnlyMemory<byte>> Pcm16StreamObservable { get; }

    void StartRecording();
    void StopRecording();
}
