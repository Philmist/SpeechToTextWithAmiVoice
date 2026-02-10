using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SpeechToTextWithAmiVoice.ViewModels;
using System;
using System.Threading.Tasks;

namespace SpeechToTextWithAmiVoice.Views
{
    public partial class SpeechToTextView : UserControl
    {
        private SpeechToTextViewModel? viewModel;

        public SpeechToTextView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (viewModel is not null)
            {
                viewModel.OpenSettingsRequested -= OnOpenSettingsRequested;
            }

            viewModel = DataContext as SpeechToTextViewModel;
            if (viewModel is not null)
            {
                viewModel.OpenSettingsRequested += OnOpenSettingsRequested;
            }

            base.OnDataContextChanged(e);
        }

        private async void OnOpenSettingsRequested(object? sender, EventArgs e)
        {
            await OpenSettingsDialogAsync();
        }

        private async Task OpenSettingsDialogAsync()
        {
            if (App.Services is null)
            {
                return;
            }

            var vm = App.Services.GetRequiredService<SettingsPrefViewModel>();
            var window = new SettingsPrefWindow
            {
                DataContext = vm
            };

            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is not null)
            {
                await window.ShowDialog(owner);
            }
            else
            {
                window.Show();
            }
        }
    }
}
