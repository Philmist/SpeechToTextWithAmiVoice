using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SpeechToText.Core;
using SpeechToTextWithAmiVoice.Services;
using SpeechToTextWithAmiVoice.ViewModels;
using SpeechToTextWithAmiVoice.Views;
using System;

namespace SpeechToTextWithAmiVoice
{
    public class App : Application
    {
        public static IServiceProvider? Services { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var collection = new ServiceCollection();
            collection.AddSingleton<ISettingsStore, JsonSettingsStore>();
            collection.AddSingleton<RecognitionSessionCoordinator>();
            collection.AddSingleton<RecognitionResultDispatcher>();
            collection.AddTransient<MainWindowViewModel>();
            collection.AddTransient<SpeechToTextViewModel>();
            collection.AddTransient<SettingsPrefViewModel>();
            collection.AddSingleton<IAudioCaptureServiceFactory, WasapiAudioCaptureServiceFactory>();
            collection.AddHttpClient();
            Services = collection.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var vm = Services.GetRequiredService<MainWindowViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = vm
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
