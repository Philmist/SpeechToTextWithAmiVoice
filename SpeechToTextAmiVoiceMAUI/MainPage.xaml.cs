using SpeechToTextAmiVoiceMAUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace SpeechToTextAmiVoiceMAUI;

public partial class MainPage : ContentPage
{
    private readonly IServiceProvider serviceProvider;

    public MainPage(MainPageViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.serviceProvider = serviceProvider;
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        var settingsPage = serviceProvider.GetRequiredService<SettingsPage>();
        await Navigation.PushAsync(settingsPage);
    }
}
