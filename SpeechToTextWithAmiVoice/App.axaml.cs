using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SpeechToText.Core;
using SpeechToTextWithAmiVoice.ViewModels;
using SpeechToTextWithAmiVoice.Views;

namespace SpeechToTextWithAmiVoice
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }
        public override void OnFrameworkInitializationCompleted()
        {
            var collection = new ServiceCollection();
            collection.AddTransient<MainWindowViewModel>();
            collection.AddTransient<SpeechToTextViewModel>();
            collection.AddSingleton<IAudioCaptureServiceFactory, WasapiAudioCaptureServiceFactory>();
            collection.AddHttpClient();
            var services = collection.BuildServiceProvider();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var vm = services.GetRequiredService<MainWindowViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = vm,
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
