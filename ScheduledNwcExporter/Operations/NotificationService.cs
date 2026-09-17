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
            bool wasCancelled = summary.Cancelled > 0;
            bool isScheduled = summary.TriggerSource == SessionTriggerSource.Scheduler;

            string title;
            ToolTipIcon icon;

            if (hasFailure)
            {
                title = "Hatco NWC Exporter - Attention";
                icon = ToolTipIcon.Warning;
            }
            else if (wasCancelled)
            {
                title = "Hatco NWC Exporter - Cancelled";
                icon = ToolTipIcon.Info;
            }
            else
            {
                title = "Hatco NWC Exporter - Completed";
                icon = ToolTipIcon.Info;
            }

            string triggerLabel = isScheduled ? "Scheduled batch" : "Manual batch";
            string text;

            if (hasFailure)
            {
                text = $"{triggerLabel} finished: {summary.Successful} successful, {summary.Failed} failed, {summary.Skipped} skipped.";
            }
            else if (wasCancelled)
            {
                text = $"{triggerLabel} stopped: {summary.Successful} successful, {summary.Cancelled} cancelled.";
            }
            else
            {
                text = $"{triggerLabel} completed: {summary.Successful} successful, {summary.Skipped} skipped.";
            }

            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = icon;
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
