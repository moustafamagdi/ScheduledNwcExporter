using System;
using System.Linq;
using System.Text;
using System.Windows;
using ScheduledNwcExporter.Application;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Operations;
using ScheduledNwcExporter.Reliability;

namespace ScheduledNwcExporter.UI.Views
{
    public partial class DashboardWindow : Window
    {
        public DashboardWindow()
        {
            InitializeComponent();
            RefreshMetrics();
        }

        private void RefreshMetrics()
        {
            AppSettings settings = App.ConfigManager?.CurrentSettings ?? new AppSettings();
            var jobs = settings.Jobs ?? new System.Collections.Generic.List<ModelExportJob>();

            int current = jobs.Count(job => !FreshnessEvaluator.Evaluate(job, settings).NeedsExport);
            int needsExport = jobs.Count(job => FreshnessEvaluator.Evaluate(job, settings).NeedsExport);
            int failed = jobs.Count(job => job.Status == JobStatus.Failed || job.LatestRunStatus == JobStatus.Failed);
            int enabled = jobs.Count(job => job.IsEnabled);

            CurrentCountText.Text = current.ToString();
            NeedsExportCountText.Text = needsExport.ToString();
            FailedCountText.Text = failed.ToString();
            EnabledCountText.Text = enabled.ToString();

            var recent = SessionHistoryService.GetRecent(30);
            var durations = recent
                .SelectMany(session => session.Jobs)
                .Select(job => TimeSpan.TryParse(job.Duration, out TimeSpan duration) ? (TimeSpan?)duration : null)
                .Where(duration => duration.HasValue)
                .Select(duration => duration.Value)
                .ToList();

            if (durations.Count > 0)
            {
                TimeSpan average = TimeSpan.FromSeconds(durations.Average(duration => duration.TotalSeconds));
                AverageTimeText.Text = average.TotalMinutes >= 1
                    ? $"{(int)average.TotalMinutes}m {average.Seconds}s"
                    : $"{average.Seconds}s";
            }
            else
            {
                AverageTimeText.Text = "—";
            }

            SessionHistoryRecord latest = SessionHistoryService.GetLatest();
            LastBatchText.Text = latest == null
                ? "No recorded batch yet."
                : $"{latest.StartedDisplay} · {latest.TriggerSource}\n{latest.ResultDisplay} · {latest.Duration}";

            SessionHistoryRecord scheduled = SessionHistoryService.GetLatestScheduled();
            LastScheduledText.Text = scheduled == null
                ? "No scheduled batch recorded yet."
                : $"{scheduled.StartedDisplay}\n{scheduled.ResultDisplay} · {scheduled.Duration}";

            var builder = new StringBuilder();
            foreach (SessionHistoryRecord session in recent.Take(5))
            {
                builder.AppendLine($"{session.StartedDisplay} · {session.TriggerSource} · {session.ResultDisplay} · {session.Duration}");
            }
            RecentActivityText.Text = builder.Length > 0 ? builder.ToString().TrimEnd() : "No recent activity recorded.";
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshMetrics();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
