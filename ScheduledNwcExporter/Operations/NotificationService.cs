using System;
using System.Drawing;
using System.Windows.Forms;
using ScheduledNwcExporter.Revit.ExternalEvents;

namespace ScheduledNwcExporter.Operations
{
    public sealed class NotificationService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;

        public NotificationService()
        {
            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "Hatco NWC Exporter",
                Icon = SystemIcons.Application
            };
        }

        public void NotifySessionCompleted(ExportSessionSummary summary)
        {
            if (summary == null) return;

            bool hasFailure = summary.Failed > 0 || !string.IsNullOrWhiteSpace(summary.SessionError);
            bool shouldNotify = hasFailure || summary.TriggerSource == SessionTriggerSource.Scheduler;
            if (!shouldNotify) return;

            string title = hasFailure ? "Hatco NWC Exporter - Attention" : "Hatco NWC Exporter - Completed";
            string text = hasFailure
                ? $"Batch {summary.SessionId}: {summary.Failed} failed, {summary.Successful} successful."
                : $"Scheduled batch completed: {summary.Successful} successful, {summary.Skipped} skipped.";

            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = hasFailure ? ToolTipIcon.Warning : ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(7000);
        }

        public void NotifyPreflightBlocked(string message)
        {
            _notifyIcon.BalloonTipTitle = "Hatco NWC Exporter - Scheduled run blocked";
            _notifyIcon.BalloonTipText = string.IsNullOrWhiteSpace(message) ? "Operational preflight blocked the scheduled run." : message;
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
            _notifyIcon.ShowBalloonTip(7000);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
