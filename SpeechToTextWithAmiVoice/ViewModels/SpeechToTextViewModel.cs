using NAudio.CoreAudioApi;
using NAudio.Wave;
using ReactiveUI;
using SharpDX.Direct3D11;
using SpeechToTextWithAmiVoice.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;

namespace SpeechToTextWithAmiVoice.ViewModels
{
    class SpeechToTextViewModel : ViewModelBase
    {
        private AmiVoiceAPI amiVoiceAPI;
        public AmiVoiceAPI AmiVoiceAPI
        {
            get => amiVoiceAPI;
            set => this.RaiseAndSetIfChanged(ref amiVoiceAPI, value);
        }

        private string statusText;
        public string StatusText
        {
            get => statusText;
            set => this.RaiseAndSetIfChanged(ref statusText, value);
        }
        private string recognizedText;
        public string RecognizedText
        {
            get => recognizedText;
            set => this.RaiseAndSetIfChanged(ref recognizedText, value);
        }

        private SpeechToTextSettings speechToTextSettings;
        public SpeechToTextSettings SpeechToTextSettings
        {
            get => speechToTextSettings;
            set => this.RaiseAndSetIfChanged(ref speechToTextSettings, value);

        }

        public ObservableCollection<MMDevice> WaveInDeviceItems { get; set; }
        private MMDevice selectedWaveInDevice;
        public MMDevice SelectedWaveInDevice
        {
            get => selectedWaveInDevice;
            set => this.RaiseAndSetIfChanged(ref selectedWaveInDevice, value);
        }

        private double waveMaxValue;
        public double WaveMaxValue
        {
            get => waveMaxValue;
            set => this.RaiseAndSetIfChanged(ref waveMaxValue, value);
        }

        public ReactiveCommand<Unit, Unit> OnClickFileSelectButtonCommand { get; }
        public ReactiveCommand<Unit, Unit> OnClickRecordButtonCommand
        {
            get => onClickRecordButtonCommand;
            set => this.RaiseAndSetIfChanged(ref onClickRecordButtonCommand, value);
        }
        protected ReactiveCommand<Unit, Unit> onClickRecordButtonCommand;
        private ReactiveCommand<Unit, Unit> StartRecordingCommand;
        private ReactiveCommand<Unit, Unit> StopRecordingCommand;
        private bool isRecording;
        private IDisposable disposableWaveInObservable;
        private IDisposable disposableWaveMaxObservable;

        private CaptureVoiceFromWasapi captureVoice;

        private string recordButtonText;
        public string RecordButtonText
        {
            get => recordButtonText;
            set => this.RaiseAndSetIfChanged(ref recordButtonText, value);
        }

        private void changeButtonToStartRecording()
        {
            RecordButtonText = "Start";
            OnClickRecordButtonCommand = StartRecordingCommand;
        }

        private void changeButtonToStopRecording()
        {
            RecordButtonText = "Stop";
            OnClickRecordButtonCommand = StopRecordingCommand;
        }

        private WaveFileWriter waveFileWriter;
        private VoiceRecognizerWithAmiVoiceCloud voiceRecognizer;
        private CancellationTokenSource tokenSource;

        public SpeechToTextViewModel()
        {
            AmiVoiceAPI = new AmiVoiceAPI { WebSocketURI = "", AppKey = "" };

            StatusText = "Status";
            RecognizedText = "";

            SpeechToTextSettings = new SpeechToTextSettings();
            SpeechToTextSettings.OutputClearingIsEnabled = true;
            SpeechToTextSettings.OutputClearingSeconds = 0;
            SpeechToTextSettings.OutputTextfilePath = "";

            var deviceEnum = new MMDeviceEnumerator();
            var devices = deviceEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
            WaveInDeviceItems = new ObservableCollection<MMDevice>(devices);
            SelectedWaveInDevice = WaveInDeviceItems.First();
            captureVoice = new CaptureVoiceFromWasapi(SelectedWaveInDevice);
            disposableWaveInObservable = null;

            OnClickFileSelectButtonCommand = ReactiveCommand.Create(() =>
            {
                var text = String.Format("Selected Device: {0}\nAppKey: {1}", SelectedWaveInDevice.FriendlyName, amiVoiceAPI.AppKey);
                RecognizedText = text;
                captureVoice = new CaptureVoiceFromWasapi(SelectedWaveInDevice);
            });

            StartRecordingCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (isRecording == true)
                {
                    changeButtonToStopRecording();
                    return;
                }

                try
                {

                    voiceRecognizer = new VoiceRecognizerWithAmiVoiceCloud(amiVoiceAPI.WebSocketURI.Trim(), amiVoiceAPI.AppKey.Trim());

                }
                catch (Exception ex)
                {
                    isRecording = false;
                    changeButtonToStartRecording();
                    Debug.WriteLine(ex);
                    return;
                }

                /*
                try
                {
                    var connectionResult = await voiceRecognizer.Connect(CancellationToken.None);
                    if (connectionResult.isSuccess != true)
                    {
                        throw new Exception(connectionResult.message);
                    }
                    Debug.WriteLine(connectionResult.message);
                    RecognizedText = String.Format("Connect: {0}", connectionResult.message);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    RecognizedText = ex.Message;
                    changeButtonToStartRecording();
                    return;
                }
                */

                captureVoice = new CaptureVoiceFromWasapi(SelectedWaveInDevice);

                /*
                waveFileWriter = new WaveFileWriter("test.wav", captureVoice.TargetWaveFormat);
                Debug.WriteLine(captureVoice.TargetWaveFormat.ToString());
                disposableWaveInObservable = captureVoice.Pcm16StreamObservable.Subscribe((b) =>
                {
                    // Debug.WriteLine(String.Format("Subscribed Data Coming: {0}", b.Length));
                    waveFileWriter.Write(b);
                });
                */
                disposableWaveMaxObservable = Observable.FromEvent<EventHandler<float>, float>(
                    h => (s, e) => h(e),
                    h => captureVoice.ResampledMaxValueAvailable += h,
                    h => captureVoice.ResampledMaxValueAvailable -= h
                    ).Subscribe((v) => { WaveMaxValue = v; });
                captureVoice.StartRecording();
                changeButtonToStopRecording();
                isRecording = true;
            });

            StopRecordingCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (isRecording == false)
                {
                    changeButtonToStartRecording();
                    return;
                }

                try
                {
                    /*
                    var result = await voiceRecognizer.Disconnect(CancellationToken.None);
                    Debug.WriteLine(result.message);
                    RecognizedText = String.Format("message: {0}", result.message);
                    */
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    /*
                    disposableWaveInObservable?.Dispose();
                    */
                    /*
                    waveFileWriter?.Dispose();
                    */
                    disposableWaveMaxObservable?.Dispose();

                    captureVoice.StopRecording();

                    captureVoice = null;
                    changeButtonToStartRecording();
                    isRecording = false;
                    StatusText = "Stopped";
                    WaveMaxValue = 0;

                }
            });

            changeButtonToStartRecording();
            isRecording = false;
        }
    }
}
