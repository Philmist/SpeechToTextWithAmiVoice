using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpeechToText.Core;
using SpeechToTextAmiVoiceMAUI.Services;
using SpeechToTextAmiVoiceMAUI.ViewModels;
#if WINDOWS
using SpeechToTextAmiVoiceMAUI.Platforms.Windows;
#endif

namespace SpeechToTextAmiVoiceMAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Register audio capture factory by platform.
#if WINDOWS
        builder.Services.AddSingleton<IAudioCaptureServiceFactory, WasapiMauiAudioCaptureServiceFactory>();
#else
        builder.Services.AddSingleton<IAudioCaptureServiceFactory, UnsupportedAudioCaptureServiceFactory>();
#endif
        builder.Services.AddSingleton<ISettingsStore, SettingsStore>();
        builder.Services.AddSingleton<RecognitionSessionCoordinator>();
        builder.Services.AddSingleton<RecognitionResultDispatcher>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<MainPageViewModel>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<SettingsPageViewModel>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
