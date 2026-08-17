using System;
using System.Reflection;
using System.Text;
using System.Windows;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Revit;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.UI.ViewModels;

namespace ScheduledNwcExporter.UI.Views
{
    public partial class DiagnosticsWindow : Window
    {
        private readonly DiagnosticsViewModel _viewModel;

        public DiagnosticsWindow(ILogger logger, AppSettings settings)
        {
            InitializeComponent();
            _viewModel = new DiagnosticsViewModel(logger, settings);
            DataContext = _viewModel;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Hatco NWC Exporter Diagnostics Report ===");
            sb.AppendLine($"Generated: {DateTime.Now}");
            sb.AppendLine("-------------------------------------------");
            foreach (var test in _viewModel.Tests)
            {
                sb.AppendLine($"[{test.StatusIcon}] {test.Title}");
                sb.AppendLine($"Details: {test.Details}");
                sb.AppendLine();
            }
            
            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Diagnostics report copied to clipboard.", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class InverseBooleanConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool b && !b;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool b && !b;
        }
    }
}
