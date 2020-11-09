﻿/*
 * CaptureVoiceFromWasapi.cs
 * 
 * Copyright 2020 Philmist
 * 
 * Copyright 2017 zufall (from: https://github.com/zufall-upon/kikisen-vc)
 * 
 * This file is part of SpeechToTextAmiVoice.
 * SpeechToTextAmiVoice is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * Foobar is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with Foobar.  If not, see <http://www.gnu.org/licenses/>.
 */

using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.IO;
using System.Reactive.Linq;

namespace SpeechToTextWithAmiVoice
{
    class CaptureVoiceFromWasapi
    {

        private MMDevice CaptureTargetDevice;
        private WasapiCapture capture;

        public readonly WaveFormat TargetWaveFormat;

        public event EventHandler<byte[]> ResampledDataAvailable;

        public IObservable<byte[]> Pcm16StreamObservable { get; }

        /// <summary>
        /// Event handler to capture waspi device and convert to pcm16.
        /// </summary>
        /// <remarks>
        /// see also: https://qiita.com/zufall/items/2e027a2bc996864fe4af
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void WaspiDataAvailable(object sender, WaveInEventArgs eventArgs)
        {
            if (eventArgs.BytesRecorded == 0)
            {
                ResampledDataAvailable(this, new byte[0]);
                return;
            }

            using (var memStream = new MemoryStream(eventArgs.Buffer, 0, eventArgs.BytesRecorded))
            {
                using (var inputStream = new RawSourceWaveStream(memStream, capture.WaveFormat))
                {
                    var sampleStream = new WaveToSampleProvider(inputStream);
                    var resamplingProvider = new WdlResamplingSampleProvider(sampleStream, TargetWaveFormat.SampleRate);
                    var pcmProvider = new SampleToWaveProvider16(resamplingProvider);
                    IWaveProvider targetProvider;
                    if (capture.WaveFormat.Channels != 1)
                    {
                        var stereoToMonoProvider = new StereoToMonoProvider16(pcmProvider);
                        stereoToMonoProvider.RightVolume = 0.5f;
                        stereoToMonoProvider.LeftVolume = 0.5f;
                        targetProvider = stereoToMonoProvider;
                    }
                    else
                    {
                        targetProvider = pcmProvider;
                    }

                    byte[] buffer = new byte[eventArgs.BytesRecorded];
                    using (var outputStream = new MemoryStream())
                    {
                        int readBytes;
                        while ((readBytes = targetProvider.Read(buffer, 0, eventArgs.BytesRecorded)) > 0)
                        {
                            outputStream.Write(buffer, 0, readBytes);
                        }
                        ResampledDataAvailable(this, outputStream.ToArray());
                    }
                }
            }
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
