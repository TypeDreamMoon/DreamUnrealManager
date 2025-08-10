using Microsoft.UI.Xaml.Data;
using System;

namespace DreamUnrealManager.Helpers
{
    public class DateTimeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dateTime)
            {
                var timeDiff = DateTime.Now - dateTime;

                if (timeDiff.TotalDays < 1)
                    return "今天";
                else if (timeDiff.TotalDays < 7)
                    return $"{(int)timeDiff.TotalDays} 天前";
                else if (timeDiff.TotalDays < 30)
                    return $"{(int)(timeDiff.TotalDays / 7)} 周前";
                else
                    return dateTime.ToString("yyyy/MM/dd");
            }

            return "未知";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}