using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SpeechToTextWithAmiVoice.ViewModels;
using System;

namespace SpeechToTextWithAmiVoice;

public partial class SettingsPrefWindow : Window
{
    private SettingsPrefViewModel? viewModel;

    public SettingsPrefWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        viewModel = DataContext as SettingsPrefViewModel;
        if (viewModel is not null)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }

        base.OnDataContextChanged(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
