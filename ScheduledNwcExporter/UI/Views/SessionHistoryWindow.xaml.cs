using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ScheduledNwcExporter.Operations;

namespace ScheduledNwcExporter.UI.Views
{
    public partial class SessionHistoryWindow : Window
    {
        public SessionHistoryWindow()
        {
            InitializeComponent();
            LoadHistory();
        }

        private void LoadHistory()
        {
            HistoryGrid.ItemsSource = SessionHistoryService.GetRecent(100);
            if (HistoryGrid.Items.Count > 0 && HistoryGrid.SelectedItem == null)
                HistoryGrid.SelectedIndex = 0;
        }

        private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(HistoryGrid.SelectedItem is SessionHistoryRecord session))
            {
                DetailsText.Text = string.Empty;
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine($"Session: {session.SessionId}");
            builder.AppendLine($"Trigger: {session.TriggerSource}");
            builder.AppendLine($"Started: {session.StartedAt:dd MMM yyyy HH:mm:ss}");
            builder.AppendLine($"Ended: {session.EndedAt:dd MMM yyyy HH:mm:ss}");
            builder.AppendLine($"Duration: {session.Duration}");
            if (!string.IsNullOrWhiteSpace(session.SessionError))
                builder.AppendLine($"Session error: {session.SessionError}");
            builder.AppendLine();

            foreach (SessionJobHistoryRecord job in session.Jobs)
            {
                builder.AppendLine($"[{job.Status}] {job.ModelName} ({job.Duration})");
                builder.AppendLine($"  Before: {job.FreshnessBefore}");
                builder.AppendLine($"  After : {job.FreshnessAfter}");
                if (!string.IsNullOrWhiteSpace(job.OutputPath)) builder.AppendLine($"  Output: {job.OutputPath}");
                if (!string.IsNullOrWhiteSpace(job.ErrorMessage)) builder.AppendLine($"  Error : {job.ErrorMessage}");
            }

            DetailsText.Text = builder.ToString();
        }

        private void OpenReport_Click(object sender, RoutedEventArgs e)
        {
            if (!(HistoryGrid.SelectedItem is SessionHistoryRecord session) || string.IsNullOrWhiteSpace(session.ReportPath) || !File.Exists(session.ReportPath))
            {
                MessageBox.Show("The selected session report is not available.", "Hatco NWC Exporter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo(session.ReportPath) { UseShellExecute = true });
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadHistory();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
