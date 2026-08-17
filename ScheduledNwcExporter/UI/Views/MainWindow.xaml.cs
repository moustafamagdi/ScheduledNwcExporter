using System;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Autodesk.Revit.UI;
using ScheduledNwcExporter.Application;
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
        private readonly ILogger _logger;

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                // AUDIT FIX: Use App-level services for consistent lifecycle
                _logger = App.Logger ?? new FileLogger();
                _exportQueueHandler = App.QueueHandler ?? throw new InvalidOperationException("Queue handler not initialized.");
                _exportQueueEvent = App.QueueEvent ?? throw new InvalidOperationException("External event not initialized.");

                // Attach a global exception handler for this window's dispatcher
                this.Dispatcher.UnhandledException += Dispatcher_UnhandledException;

                _viewModel = new MainViewModel(App.ConfigManager ?? new ConfigurationManager(), _logger, _exportQueueHandler, App.Scheduler);
                DataContext = _viewModel;
                Closed += MainWindow_Closed;

                // Register interactive event handlers
                RegisterSafeEventHandlers();
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

        private void Dispatcher_UnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.Error("UI", $"Unhandled exception caught: {e.Exception.Message}", string.Empty, "Dispatcher", e.Exception);
            Autodesk.Revit.UI.TaskDialog.Show("Hatco NWC Exporter - Error", $"An unexpected UI error occurred:\n{e.Exception.Message}\n\nThe application will attempt to continue, but please check the logs.");
            e.Handled = true;
        }

        private void RegisterSafeEventHandlers()
        {
            // Enable single-click checkbox toggling on DataGrid
            QueueDataGrid.PreviewMouseLeftButtonDown += (s, e) =>
            {
                try
                {
                    var dep = e.OriginalSource as DependencyObject;
                    while (dep != null && !(dep is DataGridCell) && !(dep is DataGridColumnHeader))
                    {
                        dep = VisualTreeHelper.GetParent(dep);
                    }

                    if (dep is DataGridCell cell && cell.Column is DataGridCheckBoxColumn)
                    {
                        if (cell.DataContext is ModelExportJob job)
                        {
                            bool newState = !job.IsEnabled;
                            
                            // Support bulk toggling if multiple rows are selected
                            if (QueueDataGrid.SelectedItems.Count > 1 && QueueDataGrid.SelectedItems.Contains(job))
                            {
                                var selectedJobs = QueueDataGrid.SelectedItems.Cast<object>()
                                    .OfType<ModelExportJob>()
                                    .ToList();

                                foreach (var selectedJob in selectedJobs)
                                {
                                    selectedJob.IsEnabled = newState;
                                }
                            }
                            else
                            {
                                job.IsEnabled = newState;
                            }
                            
                            e.Handled = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error("UI", $"Error in checkbox click handler: {ex.Message}", string.Empty, "Interaction");
                }
            };

            // Support bulk toggling with Space key
            QueueDataGrid.PreviewKeyDown += (s, e) =>
            {
                try
                {
                    if (e.Key == System.Windows.Input.Key.Space && QueueDataGrid.SelectedItems.Count > 0)
                    {
                        var selectedJobs = QueueDataGrid.SelectedItems.Cast<object>()
                            .OfType<ModelExportJob>()
                            .ToList();

                        if (selectedJobs.Any())
                        {
                            bool newState = !selectedJobs.First().IsEnabled;
                            foreach (var job in selectedJobs)
                            {
                                job.IsEnabled = newState;
                            }
                            e.Handled = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error("UI", $"Error in key down handler: {ex.Message}", string.Empty, "Interaction");
                }
            };
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _viewModel?.Shutdown();
                // AUDIT FIX: Do NOT dispose App-level event here, just detach VM listeners
            }
            catch { /* Ignore cleanup errors */ }
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

    public class BoolToCloudIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool isCloud && isCloud ? "☁️" : "💻";
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
            try
            {
                if (value is JobStatus status)
                {
                    switch (status)
                    {
                        case JobStatus.Success:
                            return new SolidColorBrush(Color.FromRgb(144, 238, 144)); // LightGreen
                        case JobStatus.Processing:
                        case JobStatus.Retrying:
                            return new SolidColorBrush(Color.FromRgb(152, 251, 152)); // PaleGreen
                        case JobStatus.Failed:
                            return new SolidColorBrush(Color.FromRgb(255, 235, 235)); // Very light red
                        case JobStatus.Cancelled:
                            return new SolidColorBrush(Color.FromRgb(255, 250, 205)); // LemonChiffon
                        case JobStatus.Skipped:
                            return new SolidColorBrush(Color.FromRgb(245, 245, 245)); // WhiteSmoke
                        default:
                            return Brushes.Transparent;
                    }
                }
            }
            catch { }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
