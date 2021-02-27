using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ReactiveUI;
using SpeechToTextWithAmiVoice.Models;
using SpeechToTextWithAmiVoice.Views;
using System;
using System.Collections.Generic;
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
        private IDisposable disposableRecognizerErrorObservable;
        private IDisposable disposableRecognizerRecognizeObservable;
        private IDisposable disposableRecognizerStopped;

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


            OnClickFileSelectButtonCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var fileDialog = new SaveFileDialog();
                fileDialog.DefaultExtension = ".txt";
                var mainWindow = (App.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                var fileStr = await fileDialog.ShowAsync(mainWindow);
                var afterObj = new SpeechToTextSettings(SpeechToTextSettings);
                afterObj.OutputTextfilePath = fileStr;
                SpeechToTextSettings = afterObj;
            });

            StartRecordingCommand = ReactiveCommand.Create(() =>
            {
                tokenSource = new CancellationTokenSource();

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

                captureVoice = new CaptureVoiceFromWasapi(SelectedWaveInDevice);
                Debug.WriteLine(captureVoice);

                disposableWaveMaxObservable = Observable.FromEvent<EventHandler<float>, float>(
                    h => (s, e) => h(e),
                    h => captureVoice.ResampledMaxValueAvailable += h,
                    h => captureVoice.ResampledMaxValueAvailable -= h
                    ).Subscribe((v) => { WaveMaxValue = v; });
                captureVoice.StartRecording();
                changeButtonToStopRecording();
                isRecording = true;

                try
                {
                    var ct = tokenSource.Token;

                    disposableWaveInObservable = captureVoice.Pcm16StreamObservable.Subscribe(
                        (b) =>
                        {
                            voiceRecognizer?.FeedRawWave(b);
                        }
                        );

                    var completeObservable = Observable.FromEvent<EventHandler<VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs>, VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs>(
                        h => (s, e) => h(e),
                        h =>
                        {
                            voiceRecognizer.Recognized += h;
                        },
                        h =>
                        {
                            voiceRecognizer.Recognized -= h;
                        }
                        );
                    var progressObservable = Observable.FromEvent<EventHandler<VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs>, VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs>(
                        h => (s, e) => h(e),
                        h =>
                        {
                            voiceRecognizer.Recognizing += h;
                        },
                        h =>
                        {
                            voiceRecognizer.Recognizing -= h;
                        }
                        );
                    var observables = new List<IObservable<VoiceRecognizerWithAmiVoiceCloud.SpeechRecognitionEventArgs>> { completeObservable };
                    disposableRecognizerRecognizeObservable = Observable.Concat(observables)
                    .Subscribe(r =>
                    {
                        Debug.WriteLine(r.Text);
                        if (String.IsNullOrEmpty(r.code))  // エラーがないならコードは空文字列
                        {
                            RecognizedText = r.Text;
                        }
                    });

                    disposableRecognizerErrorObservable = Observable.FromEvent<EventHandler<string>, string>(
                        h => (s, e) => h(e),
                        h => { voiceRecognizer.ErrorOccured += h; },
                        h => { voiceRecognizer.ErrorOccured -= h; }
                        )
                    .Subscribe(err =>
                    {
                        StatusText = err;
                    });

                    disposableRecognizerStopped = Observable.FromEvent<EventHandler<bool>, bool>(
                        h => (s, e) => h(e),
                        h => { voiceRecognizer.RecognizeStopped += h; },
                        h => { voiceRecognizer.RecognizeStopped += h; }
                        )
                    .Subscribe((r) =>
                    {
                        this.StopRecordingCommand.Execute().Subscribe();
                    });

                    voiceRecognizer.Start(ct);
                    StatusText = "Start";
                }
                catch (Exception ex)
                {
                    disposableWaveInObservable?.Dispose();
                    disposableRecognizerErrorObservable?.Dispose();
                    disposableRecognizerRecognizeObservable?.Dispose();
                    Debug.WriteLine(ex.Message);
                    throw;
                }
            });

            StopRecordingCommand = ReactiveCommand.Create(() =>
            {
                if (isRecording == false)
                {
                    changeButtonToStartRecording();
                    return;
                }

                try
                {
                    tokenSource.Cancel();
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    disposableWaveInObservable?.Dispose();
                    disposableWaveMaxObservable?.Dispose();
                    disposableRecognizerRecognizeObservable?.Dispose();
                    disposableRecognizerErrorObservable?.Dispose();
                    disposableRecognizerStopped?.Dispose();

                    captureVoice.StopRecording();

                    changeButtonToStartRecording();
                    isRecording = false;
                    WaveMaxValue = 0;

                }
            });

            changeButtonToStartRecording();
            isRecording = false;
        }
    }
}
