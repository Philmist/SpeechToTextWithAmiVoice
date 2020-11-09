using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SpeechToTextWithAmiVoice
{
    class DoubleToDateTimeDeltaStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double v;
            try
            {
                v = (double)value;
            }
            catch (InvalidCastException)
            {
                return Avalonia.Data.BindingNotification.UnsetValue;
            }
            int seconds = (int)(v % 60);
            int minutes = (int)((v - seconds) / 60);
            return String.Format("{0}m{1}s", minutes, seconds);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Avalonia.Data.BindingNotification.Null;
        }
    }
}
