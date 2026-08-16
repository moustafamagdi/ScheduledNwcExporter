using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.UI;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Revit.ExternalEvents;
using ScheduledNwcExporter.UI.ViewModels;

namespace ScheduledNwcExporter.UI.Views
{
    /// <summary>
    /// Modeless WPF owner for the ExternalEvent used to access the Revit API.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ExternalEvent _exportQueueEvent;
        private readonly ExportQueueExternalEventHandler _exportQueueHandler;
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                var configurationManager = new ConfigurationManager();
                var logger = new FileLogger
                {
                    DebugMode = configurationManager.CurrentSettings.DebugMode
                };

                _exportQueueHandler = new ExportQueueExternalEventHandler(logger, configurationManager.CurrentSettings, Dispatcher);
                _exportQueueEvent = ExternalEvent.Create(_exportQueueHandler);
                _exportQueueHandler.AttachExternalEvent(_exportQueueEvent);

                _viewModel = new MainViewModel(configurationManager, logger, _exportQueueHandler);
                DataContext = _viewModel;
                Closed += MainWindow_Closed;

                // Enable single-click checkbox toggling on DataGrid
                QueueDataGrid.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    var dep = (System.Windows.DependencyObject)e.OriginalSource;
                    while (dep != null && !(dep is System.Windows.Controls.DataGridCell) && !(dep is System.Windows.Controls.Primitives.DataGridColumnHeader))
                    {
                        dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
                    }

                    if (dep is System.Windows.Controls.DataGridCell cell && cell.Column is System.Windows.Controls.DataGridCheckBoxColumn)
                    {
                        if (!cell.IsEditing)
                        {
                            cell.IsSelected = true;
                            if (cell.DataContext is ScheduledNwcExporter.Configuration.ModelExportJob job)
                            {
                                job.IsEnabled = !job.IsEnabled;
                                e.Handled = true;
                            }
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                FileLogger? fallbackLogger = null;
                try { fallbackLogger = new FileLogger(); } catch { }
                fallbackLogger?.Error("UI", $"Critical initialization failure: {ex.Message}", string.Empty, "Startup", ex);
                Autodesk.Revit.UI.TaskDialog.Show("Hatco NWC Exporter", $"Critical startup error:\n{ex.Message}\n\nCheck logs under AppData\\Roaming\\MoustafaMagdi\\ScheduledNwcExporter\\logs");
                throw;
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _viewModel.Shutdown();
            _exportQueueEvent.Dispose();
        }
    }

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool isEnabled && isEnabled
                ? new SolidColorBrush(Color.FromRgb(46, 204, 113))
                : new SolidColorBrush(Color.FromRgb(149, 165, 166));
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class StatusToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string status = value as string ?? string.Empty;

            if (status == "Success")
            {
                // Darker shade of pale green for completed items
                return new SolidColorBrush(Color.FromRgb(144, 238, 144)); // LightGreen
            }

            if (status != "Ready" && status != "Skipped" && status != "Cancelled" && status != "Failed")
            {
                // Active/Processing status - PaleGreen
                return new SolidColorBrush(Color.FromRgb(152, 251, 152)); // PaleGreen
            }

            if (status == "Failed")
            {
                return new SolidColorBrush(Color.FromRgb(255, 235, 235)); // Very light red for failure
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
