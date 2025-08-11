using System;
using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DreamUnrealManager.Helpers
{
    public class ListToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // 检查输入的值是否为集合且是否为空
            if (value is IList list && list.Count == 0)
            {
                return Visibility.Collapsed;
            }

            // 如果集合不为空，返回 Visible
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // 不支持反向转换
            throw new NotImplementedException();
        }
    }
    
    public class EmptyListToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // 检查输入的值是否为集合且是否为空
            if (value is IList list && list.Count == 0)
            {
                return Visibility.Visible;
            }

            // 如果集合不为空，返回 Visible
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}