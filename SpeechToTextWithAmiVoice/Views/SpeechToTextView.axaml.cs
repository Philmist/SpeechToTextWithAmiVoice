using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SpeechToTextWithAmiVoice.Views
{
    public class SpeechToTextView : UserControl
    {
        public SpeechToTextView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
