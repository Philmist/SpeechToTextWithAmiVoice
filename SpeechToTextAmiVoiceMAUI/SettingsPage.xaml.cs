using SpeechToTextAmiVoiceMAUI.ViewModels;

namespace SpeechToTextAmiVoiceMAUI;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsPageViewModel viewModel;

    public SettingsPage(SettingsPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.CloseRequested -= OnCloseRequested;
    }

    private async void OnCloseRequested(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.LastOrDefault() == this)
        {
            await Navigation.PopAsync();
        }
    }
}
