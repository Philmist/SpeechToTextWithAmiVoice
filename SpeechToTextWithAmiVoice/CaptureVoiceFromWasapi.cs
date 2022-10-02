/*
 * CaptureVoiceFromWasapi.cs
 * 
 * Copyright 2020 Philmist 
 */

using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Collections.Generic;
using System.Linq;

namespace SpeechToTextWithAmiVoice
{
    class CaptureVoiceFromWasapi
    {

        private MMDevice CaptureTargetDevice;
        private WasapiCapture capture;

        public readonly WaveFormat TargetWaveFormat;

        public event EventHandler<byte[]> ResampledDataAvailable;
        public event EventHandler<float> ResampledMaxValueAvailable;

        public IObservable<byte[]> Pcm16StreamObservable { get; }

        /// <summary>
        /// Event handler to capture waspi devic.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void WaspiDataAvailable(object sender, WaveInEventArgs eventArgs)
        {
            if (eventArgs.BytesRecorded == 0)
            {
                ResampledDataAvailable?.Invoke(this, new byte[0]);
                ResampledMaxValueAvailable?.Invoke(this, 0);
                return;
            }

            var recordedAry = eventArgs.Buffer[new Range(0, eventArgs.BytesRecorded)];
            ResampledDataAvailable?.Invoke(this, recordedAry);

            var volumeBuffer = new WaveBuffer(recordedAry);
            var maxVolume = (float)volumeBuffer.ShortBuffer[new Range(0, recordedAry.Length / 2)].Max((v) => { return ((double)v < 0.0) ? (-1.0 * (double)v) : (double)v; });
            ResampledMaxValueAvailable?.Invoke(this, maxVolume);

        }

        public CaptureVoiceFromWasapi(MMDevice device)
        {
            TargetWaveFormat = new WaveFormat(16000, 16, 1);

            if (device.DataFlow != DataFlow.Capture && device.DataFlow != DataFlow.All)
            {
                throw new ArgumentException("Device does not have capture capatibity");
            }

            CaptureTargetDevice = device;
            capture = new WasapiCapture(CaptureTargetDevice);
            capture.ShareMode = AudioClientShareMode.Shared;
            capture.WaveFormat = TargetWaveFormat;

            Debug.WriteLine(capture.WaveFormat.Encoding);
            Debug.WriteLine(capture.WaveFormat.SampleRate);
            Debug.WriteLine(device.DeviceFriendlyName);

            capture.DataAvailable += WaspiDataAvailable;
            Pcm16StreamObservable = Observable.FromEvent<EventHandler<byte[]>, byte[]>(
                h => (s, e) => h(e),
                h => ResampledDataAvailable += h,
                h => ResampledDataAvailable -= h
                );
        }

        public void StartRecording()
        {
            capture.StartRecording();
        }

        public void StopRecording()
        {
            capture.StopRecording();
        }

    }
}
