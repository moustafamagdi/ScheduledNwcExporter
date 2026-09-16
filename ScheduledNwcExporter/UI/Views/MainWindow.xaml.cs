using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
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
        private ListSortDirection? _freshnessSortDirection;
        private bool _initialFreshnessSelectionPending = true;

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                _logger = App.Logger ?? new FileLogger();
                _exportQueueHandler = App.QueueHandler ?? throw new InvalidOperationException("Queue handler not initialized.");
                _exportQueueEvent = App.QueueEvent ?? throw new InvalidOperationException("External event not initialized.");

                this.Dispatcher.UnhandledException += Dispatcher_UnhandledException;

                _viewModel = new MainViewModel(App.ConfigManager ?? new ConfigurationManager(), _logger, _exportQueueHandler, App.Scheduler);
                DataContext = _viewModel;
                Closed += MainWindow_Closed;

                RegisterSafeEventHandlers();
                AddSelectNeedsExportButton();

                // The VM refreshes source dates automatically at startup. Select once immediately from
                // cached metadata, then re-apply after that startup refresh finishes so the checkboxes
                // always represent the latest known freshness state.
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                SelectNeedsExportModels(false);
                if (!_viewModel.IsRefreshingModelDates)
                {
                    SelectNeedsExportModels(true);
                    _initialFreshnessSelectionPending = false;
                }
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

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_initialFreshnessSelectionPending &&
                e.PropertyName == nameof(MainViewModel.IsRefreshingModelDates) &&
                !_viewModel.IsRefreshingModelDates)
            {
                SelectNeedsExportModels(true);
                _initialFreshnessSelectionPending = false;
            }
        }

        private void AddSelectNeedsExportButton()
        {
            // Keep the XAML layout stable: locate the queue toolbar by its existing Refresh Dates button
            // and insert the smart-selection action beside it.
            Button? refreshButton = FindButtonByContent(this, "↻ Refresh Dates");
            if (refreshButton?.Parent is Panel toolbar)
            {
                var button = new Button
                {
                    Content = " ✓ Needs Export ",
                    Width = 110,
                    Height = 28,
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = "Enable only models whose NWC is not confirmed current; disable models that are already fresh."
                };
                button.Click += (_, __) => SelectNeedsExportModels(true);

                int index = toolbar.Children.IndexOf(refreshButton);
                toolbar.Children.Insert(Math.Min(index + 1, toolbar.Children.Count), button);
            }
        }

        private static Button? FindButtonByContent(DependencyObject root, string contentText)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is Button button && string.Equals(button.Content?.ToString()?.Trim(), contentText.Trim(), StringComparison.Ordinal))
                    return button;

                Button? nested = FindButtonByContent(child, contentText);
                if (nested != null) return nested;
            }
            return null;
        }

        private void SelectNeedsExportModels(bool saveConfiguration)
        {
            int enabled = 0;
            foreach (ModelExportJob job in _viewModel.Jobs)
            {
                // A model is "fresh" only when we have both timestamps and the latest successful
                // NWC is at or after the source modification. Unknown/unverified freshness is kept ON
                // so unattended export errs on the safe side rather than silently skipping a model.
                bool isConfirmedFresh = job.LastSuccessfulExportUtc.HasValue &&
                                        job.ExportLag.HasValue &&
                                        job.ExportLag.Value.TotalMinutes <= 0;
                job.IsEnabled = !isConfirmedFresh;
                if (job.IsEnabled) enabled++;
            }

            if (saveConfiguration)
            {
                App.ConfigManager?.SaveConfiguration();
            }

            _logger?.Info("UI", $"Smart selection enabled {enabled} of {_viewModel.Jobs.Count} model(s) that need export or have unverified freshness.", string.Empty, "FreshnessSelection");
        }

        private void Dispatcher_UnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            _logger?.Error("UI", $"Unhandled exception caught: {e.Exception.Message}", string.Empty, "Dispatcher", e.Exception);
            Autodesk.Revit.UI.TaskDialog.Show("Hatco NWC Exporter - Error", $"An unexpected UI error occurred:\n{e.Exception.Message}\n\nThe application will attempt to continue, but please check the logs.");
            e.Handled = true;
        }

        private void RegisterSafeEventHandlers()
        {
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
                            if (QueueDataGrid.SelectedItems.Count > 1 && QueueDataGrid.SelectedItems.Contains(job))
                            {
                                var selectedJobs = QueueDataGrid.SelectedItems.Cast<object>().OfType<ModelExportJob>().ToList();
                                foreach (var selectedJob in selectedJobs) selectedJob.IsEnabled = newState;
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

            QueueDataGrid.PreviewKeyDown += (s, e) =>
            {
                try
                {
                    if (e.Key == System.Windows.Input.Key.Space && QueueDataGrid.SelectedItems.Count > 0)
                    {
                        var selectedJobs = QueueDataGrid.SelectedItems.Cast<object>().OfType<ModelExportJob>().ToList();
                        if (selectedJobs.Any())
                        {
                            bool newState = !selectedJobs.First().IsEnabled;
                            foreach (var job in selectedJobs) job.IsEnabled = newState;
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

        private void QueueDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (DataContext is MainViewModel viewModel)
                    viewModel.UpdateSelectedJobCount(QueueDataGrid.SelectedItems.Count);
            }
            catch (Exception ex)
            {
                _logger?.Error("UI", $"Could not update queue selection state: {ex.Message}", string.Empty, "QueueSelection", ex);
            }
        }

        private void FreshnessHeader_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is DataGridColumnHeader header) || header.Column == null) return;
                if (!(QueueDataGrid.ItemsSource is ICollectionView jobsView)) return;

                _freshnessSortDirection = _freshnessSortDirection == ListSortDirection.Descending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;
                ListSortDirection direction = _freshnessSortDirection.Value;

                using (jobsView.DeferRefresh())
                {
                    jobsView.SortDescriptions.Clear();
                    jobsView.SortDescriptions.Add(new SortDescription(nameof(ModelExportJob.FreshnessSortKey), direction));
                    jobsView.SortDescriptions.Add(new SortDescription(nameof(ModelExportJob.DisplaySourcePath), ListSortDirection.Ascending));
                }

                foreach (DataGridColumn column in QueueDataGrid.Columns)
                    column.SortDirection = ReferenceEquals(column, header.Column) ? direction : null;

                e.Handled = true;
            }
            catch (Exception ex)
            {
                _logger?.Error("UI", $"Could not sort the Freshness column: {ex.Message}", string.Empty, "FreshnessSort", ex);
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel?.Shutdown();
            }
            catch { }
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
                            return new SolidColorBrush(Color.FromRgb(144, 238, 144));
                        case JobStatus.Processing:
                        case JobStatus.Retrying:
                            return new SolidColorBrush(Color.FromRgb(152, 251, 152));
                        case JobStatus.Failed:
                            return new SolidColorBrush(Color.FromRgb(255, 235, 235));
                        case JobStatus.Cancelled:
                            return new SolidColorBrush(Color.FromRgb(255, 250, 205));
                        case JobStatus.Skipped:
                            return new SolidColorBrush(Color.FromRgb(245, 245, 245));
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

    public class RunHistoryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is List<RunResult> history && history.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Recent Run History:");
                foreach (var run in history.Take(5))
                {
                    string statusIcon = run.Status switch
                    {
                        JobStatus.Success => "✅",
                        JobStatus.Failed => "❌",
                        JobStatus.Cancelled => "⏹",
                        _ => "⚪"
                    };
                    sb.AppendLine($"{statusIcon} {run.Timestamp:MM-dd HH:mm} - {run.Status} ({run.Duration})");
                    if (!string.IsNullOrEmpty(run.ErrorMessage))
                        sb.AppendLine($"   Error: {run.ErrorMessage}");
                }
                return sb.ToString().TrimEnd();
            }
            return "No run history available.";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
