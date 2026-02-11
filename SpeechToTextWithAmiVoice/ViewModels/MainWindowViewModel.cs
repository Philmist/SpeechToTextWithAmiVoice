using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using System;
using System.Net.Http;

namespace SpeechToTextWithAmiVoice.ViewModels
{
    class MainWindowViewModel : ViewModelBase
    {
        private ViewModelBase content;
        public ViewModelBase Content
        {
            get => content;
            private set => this.RaiseAndSetIfChanged(ref content, value);
        }

        public MainWindowViewModel(SpeechToTextViewModel vm)
        {
            Content = vm;
        }
    }
}
