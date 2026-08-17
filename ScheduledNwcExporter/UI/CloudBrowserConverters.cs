using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ScheduledNwcExporter.Core;

namespace ScheduledNwcExporter.UI
{
    /// <summary>
    /// Converts CloudItemType to a representative icon character (using standard symbols).
    /// </summary>
    public class TypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CloudItemType type)
            {
                return type == CloudItemType.Folder ? "📁" : "📄";
            }
            return "❓";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Simple boolean to visibility converter.
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isVisible = value is bool b && b;
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }
}
