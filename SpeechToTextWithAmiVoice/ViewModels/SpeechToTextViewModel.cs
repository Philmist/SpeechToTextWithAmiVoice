using NAudio.CoreAudioApi;
using ReactiveUI;
using SharpDX.Direct3D11;
using SpeechToTextWithAmiVoice.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;

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

        public SpeechToTextViewModel()
        {
            AmiVoiceAPI = new AmiVoiceAPI();
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
                Debug.WriteLine(text);
                StatusText = text;
                captureVoice = new CaptureVoiceFromWasapi(SelectedWaveInDevice);
            });

            StartRecordingCommand = ReactiveCommand.Create(() =>
            {
                if (isRecording == true)
                {
                    changeButtonToStopRecording();
                    return;
                }
                captureVoice = new CaptureVoiceFromWasapi(SelectedWaveInDevice);
                changeButtonToStopRecording();
                isRecording = true;
            });

            StopRecordingCommand = ReactiveCommand.Create(() =>
            {
                if (isRecording == false)
                {
                    changeButtonToStartRecording();
                    return;
                }
                captureVoice = null;
                changeButtonToStartRecording();
                isRecording = false;
            });

            changeButtonToStartRecording();
            disposableWaveInObservable?.Dispose();
            isRecording = false;
        }
    }
}
