using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ScheduledNwcExporter.UI.ViewModels;

namespace ScheduledNwcExporter.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(Autodesk.Revit.ApplicationServices.Application app)
        {
            InitializeComponent();
            DataContext = new MainViewModel(app);
        }
    }

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEnabled && isEnabled)
            {
                return new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Green
            }
            return new SolidColorBrush(Color.FromRgb(149, 165, 166)); // Gray
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
