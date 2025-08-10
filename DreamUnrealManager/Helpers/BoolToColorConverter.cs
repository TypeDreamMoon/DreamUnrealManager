using Microsoft.UI.Xaml.Data;
using System;
using Windows.UI;

namespace DreamUnrealManager.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isValid)
            {
                return isValid ? Color.FromArgb(255, 76, 175, 80) : Color.FromArgb(255, 244, 67, 54);
            }
            return Color.FromArgb(255, 128, 128, 128);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}