using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SpeechToTextWithAmiVoice.ViewModels;
using SpeechToTextWithAmiVoice.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic;

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
