/*
 * CaptureVoiceFromWasapi.cs
 * 
 * Copyright 2020 Philmist 
 */

using NAudio.CoreAudioApi;
using NAudio.Wave;
using SpeechToText.Core;
using System;
using System.Diagnostics;
using System.Reactive.Linq;

namespace SpeechToTextWithAmiVoice
{
    sealed class CaptureVoiceFromWasapi : IAudioCaptureService
    {
        private readonly MMDevice captureTargetDevice;
        private readonly WasapiCapture capture;
        private readonly AudioMeterInformation audioMeterInformation;

        public readonly WaveFormat TargetWaveFormat;

        public event EventHandler<ReadOnlyMemory<byte>> ResampledDataAvailable;
        public event EventHandler<float> ResampledMaxValueAvailable;

        public IObservable<ReadOnlyMemory<byte>> Pcm16StreamObservable { get; }

        /// <summary>
        /// Event handler to capture waspi devic.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void WaspiDataAvailable(object sender, WaveInEventArgs eventArgs)
        {
            var data = new ReadOnlyMemory<byte>(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
            /*
            if (eventArgs.BytesRecorded == 0)
            {
                ResampledDataAvailable?.Invoke(this, new ReadOnlyMemory<byte>());
                return;
            }
            */

            //var recordedAry = eventArgs.Buffer[new Range(0, eventArgs.BytesRecorded)];
            ResampledDataAvailable?.Invoke(this, data);

            float maxVolume = audioMeterInformation.MasterPeakValue;
            ResampledMaxValueAvailable?.Invoke(this, maxVolume);

        }

        public CaptureVoiceFromWasapi(MMDevice device)
        {
            TargetWaveFormat = new WaveFormat(16000, 16, 1);

            if (device.DataFlow != DataFlow.Capture && device.DataFlow != DataFlow.All)
            {
                throw new ArgumentException("Device does not have capture capatibity");
            }

            captureTargetDevice = device;
            capture = new WasapiCapture(captureTargetDevice);
            capture.ShareMode = AudioClientShareMode.Shared;
            capture.WaveFormat = TargetWaveFormat;

            Debug.WriteLine(capture.WaveFormat.Encoding);
            Debug.WriteLine(capture.WaveFormat.SampleRate);
            Debug.WriteLine(device.DeviceFriendlyName);

            capture.DataAvailable += WaspiDataAvailable;
            Pcm16StreamObservable = Observable.FromEvent<EventHandler<ReadOnlyMemory<byte>>, ReadOnlyMemory<byte>>(
                h => (s, e) => h(e),
                h => ResampledDataAvailable += h,
                h => ResampledDataAvailable -= h
                );
            audioMeterInformation = captureTargetDevice.AudioMeterInformation;
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
            capture.DataAvailable -= WaspiDataAvailable;
            capture.Dispose();
        }
    }
}
