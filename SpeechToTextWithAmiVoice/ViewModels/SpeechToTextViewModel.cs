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
using System.Text.RegularExpressions;
using CircularBuffer;

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
        public ObservableCollection<SpeechToTextSettings.BouyomiChanVoiceMapper> BouyomiChanVoiceItems { get; set; }
        private SpeechToTextSettings.BouyomiChanVoiceMapper selectedVoice;
        public SpeechToTextSettings.BouyomiChanVoiceMapper SelectedVoice
        { 
            get => selectedVoice;
            set => this.RaiseAndSetIfChanged(ref selectedVoice, value);
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

        private string waveGaugeColor;
        public string WaveGaugeColor
        {
            get => waveGaugeColor;
            set => this.RaiseAndSetIfChanged(ref waveGaugeColor, value);
        }

        private string textOutputUri;
        public string TextOutputUri
        {
            get => textOutputUri;
            set => this.RaiseAndSetIfChanged(ref textOutputUri, value);
        }

        private bool editableIsEnable;
        public bool EditableIsEnable
        {
            get => editableIsEnable;
            set => this.RaiseAndSetIfChanged(ref editableIsEnable, value);
        }

        private bool editableIsVisible;
        public bool EditableIsVisible
        {
            get => editableIsVisible;
            set => this.RaiseAndSetIfChanged(ref editableIsVisible, value);
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
        private IDisposable disposableTraceObservable;

        private CaptureVoiceFromWasapi captureVoice;

        private string recordButtonText;
        public string RecordButtonText
        {
            get => recordButtonText;
            set => this.RaiseAndSetIfChanged(ref recordButtonText, value);
        }

        private void ChangeButtonToStartRecording()
        {
            RecordButtonText = "Start";
            OnClickRecordButtonCommand = StartRecordingCommand;
            EditableIsVisible = true;
            EditableIsEnable = true;
        }

        private void ChangeButtonToStopRecording()
        {
            RecordButtonText = "Stop";
            OnClickRecordButtonCommand = StopRecordingCommand;
        }

        private VoiceRecognizerWithAmiVoiceCloud voiceRecognizer;
        private CancellationTokenSource tokenSource;
        private BouyomiChanSender bouyomiChan;
        private RecognizedTextToFileWriter fileWriter;
        private TextHttpSender textSender;

        private void ChangeGaugeColorOn(object sender, uint v)
        {
            WaveGaugeColor = "Green";
        }

        private void ChangeGaugeColorOff(object sender, uint v)
        {
            WaveGaugeColor = "Blue";
        }

        private void ChangeGaugeColorDisable()
        {
            WaveGaugeColor = "Gray";
        }

        private const string fillerPattern = @"%(.*)%";
        private const string deletePattern = @"%%";
        const double waveVolumeMinimum = -100.0;

        public SpeechToTextViewModel()
        {
            AmiVoiceAPI = new AmiVoiceAPI { WebSocketURI = "", AppKey = "", FillerEnable = false };
            WaveMaxValue = waveVolumeMinimum;

            StatusText = "Status";
            RecognizedText = "";
            TextOutputUri = "";
            EditableIsEnable = true;
            EditableIsVisible = true;

            SpeechToTextSettings = new SpeechToTextSettings
            {
                OutputClearingIsEnabled = true,
                OutputClearingSeconds = 0,
                OutputTextfilePath = ""
            };
            BouyomiChanVoiceItems = new ObservableCollection<SpeechToTextSettings.BouyomiChanVoiceMapper>(SpeechToTextSettings.BouyomiChanVoiceMap);
            SelectedVoice = BouyomiChanVoiceItems.First();
            Debug.WriteLine(SelectedVoice.Name);
            Debug.WriteLine(BouyomiChanVoiceItems.Count);

            WaveGaugeColor = "Gray";

            var deviceEnum = new MMDeviceEnumerator();
            var devices = deviceEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
            WaveInDeviceItems = new ObservableCollection<MMDevice>(devices);
            SelectedWaveInDevice = WaveInDeviceItems.First();
            captureVoice = new CaptureVoiceFromWasapi(SelectedWaveInDevice);
            disposableWaveInObservable = null;


            OnClickFileSelectButtonCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                var fileDialog = new SaveFileDialog
                {
                    DefaultExtension = ".txt"
                };
                var mainWindow = (App.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                var fileStr = await fileDialog.ShowAsync(mainWindow);
                var afterObj = new SpeechToTextSettings(SpeechToTextSettings)
                {
                    OutputTextfilePath = fileStr
                };
                SpeechToTextSettings = afterObj;
            });

            StartRecordingCommand = ReactiveCommand.Create(() =>
            {
                tokenSource = new CancellationTokenSource();

                if (isRecording == true)
                {
                    ChangeButtonToStopRecording();
                    return;
                }

                try
                {

                    voiceRecognizer = new VoiceRecognizerWithAmiVoiceCloud(amiVoiceAPI.WebSocketURI.Trim(), amiVoiceAPI.AppKey.Trim());
                    if (!String.IsNullOrWhiteSpace(amiVoiceAPI.ProfileId))
                    {
                        voiceRecognizer.ConnectionParameter.Add("profileId", amiVoiceAPI.ProfileId.Trim());
                    }
                    if (amiVoiceAPI.FillerEnable)
                    {
                        voiceRecognizer.ConnectionParameter.Add("keepFillerToken", "1");
                    }
                }
                catch (Exception ex)
                {
                    isRecording = false;
                    ChangeButtonToStartRecording();
                    Debug.WriteLine(ex);
                    return;
                }

                captureVoice = new CaptureVoiceFromWasapi(SelectedWaveInDevice);

                bouyomiChan = new BouyomiChanSender(SpeechToTextSettings.BouyomiChanUri, SpeechToTextSettings.BouyomiChanPort, SelectedVoice.Tone);
                fileWriter = new RecognizedTextToFileWriter(SpeechToTextSettings.OutputTextfilePath);
                textSender = new TextHttpSender(TextOutputUri);

                disposableWaveMaxObservable = Observable.FromEvent<EventHandler<float>, float>(
                    h => (s, e) => h(e),
                    h => captureVoice.ResampledMaxValueAvailable += h,
                    h => captureVoice.ResampledMaxValueAvailable -= h
                    ).Subscribe((v) => {
                        double db = 0.0;
                        const double refdB = 1.0;
                        double modV = v / 32767.0;
                        if (modV > 0)
                        {
                            db = 20 * Math.Log10(modV / refdB);
                        }
                        else
                        {
                            db = waveVolumeMinimum;
                        }
                        WaveMaxValue = db;
                    });
                captureVoice.StartRecording();
                ChangeButtonToStopRecording();
                isRecording = true;

                try
                {
                    var ct = tokenSource.Token;

                    voiceRecognizer.VoiceStart += ChangeGaugeColorOn;
                    voiceRecognizer.VoiceEnd += ChangeGaugeColorOff;

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
                            var text = r.Text;
                            if (amiVoiceAPI.FillerEnable)
                            {
                                text = Regex.Replace(text, fillerPattern, @"$1");
                                text = Regex.Replace(text, deletePattern, @"");
                            }
                            RecognizedText = text;
                            _ = bouyomiChan.Send((speechToTextSettings.BouyomiChanPrefix + text).Trim());
                            _ = fileWriter.Write(text);
                            var textJson = new TextHttpSender.RecognizedText { text = text };
                            _ = textSender.Send(textJson);
                        }
                    });

                    disposableTraceObservable = Observable.FromEvent<EventHandler<string>, string>(
                        h => (s, e) => h(e),
                        h =>
                        {
                            voiceRecognizer.Trace += h;
                        },
                        h =>
                        {
                            voiceRecognizer.Trace -= h;
                        }
                        ).Subscribe(r => {
                            Debug.WriteLine(r);
                            if (!String.IsNullOrEmpty(r))
                            {
                                var nowText = DateTime.Now.ToShortTimeString();
                                StatusText = String.Format("[{0}] {1}", nowText, r.Trim());
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
                        this.StopRecordingCommand.Execute().Subscribe();
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

                    SpeechToTextSettings.BouyomiChanPrefix = SpeechToTextSettings.BouyomiChanPrefix.Trim();
                    voiceRecognizer.Start(ct);
                    StatusText = "Start";
                    EditableIsVisible = false;
                    EditableIsEnable = false;
                }
                catch (Exception ex)
                {
                    disposableWaveInObservable?.Dispose();
                    disposableRecognizerErrorObservable?.Dispose();
                    disposableRecognizerRecognizeObservable?.Dispose();
                    voiceRecognizer.VoiceStart -= ChangeGaugeColorOn;
                    voiceRecognizer.VoiceEnd -= ChangeGaugeColorOff;
                    EditableIsVisible = true;
                    EditableIsEnable = true;
                    Debug.WriteLine(ex.Message);
                    throw;
                }
            });

            StopRecordingCommand = ReactiveCommand.Create(() =>
            {
                if (isRecording == false)
                {
                    ChangeButtonToStartRecording();
                    return;
                }

                try
                {
                    tokenSource.Cancel();
                    voiceRecognizer?.messageLoopTask.Wait(1000);
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
                    disposableTraceObservable?.Dispose();

                    voiceRecognizer.VoiceStart -= ChangeGaugeColorOn;
                    voiceRecognizer.VoiceEnd -= ChangeGaugeColorOff;

                    captureVoice.StopRecording();

                    ChangeGaugeColorDisable();

                    ChangeButtonToStartRecording();
                    isRecording = false;
                    WaveMaxValue = waveVolumeMinimum;

                }
            });

            ChangeButtonToStartRecording();
            isRecording = false;
            EditableIsVisible = true;
            EditableIsEnable = true;
        }
    }
}
