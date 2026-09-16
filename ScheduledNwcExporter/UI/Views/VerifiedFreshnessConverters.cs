using System;
using System.Globalization;
using System.Windows.Data;
using ScheduledNwcExporter.Application;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Reliability;

namespace ScheduledNwcExporter.UI.Views
{
    public sealed class VerifiedFreshnessDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ModelExportJob job) || App.ConfigManager == null)
                return "? Unknown";

            FreshnessEvaluation evaluation = FreshnessEvaluator.Evaluate(job, App.ConfigManager.CurrentSettings);
            string modified = job.LastSourceModifiedUtc.HasValue
                ? " · " + job.LastSourceModifiedUtc.Value.ToLocalTime().ToString("dd MMM")
                : string.Empty;

            if (!evaluation.NeedsExport)
                return "✓ Current" + modified;

            string reason = evaluation.Reason ?? string.Empty;
            if (reason.IndexOf("No verified export snapshot", StringComparison.OrdinalIgnoreCase) >= 0)
                return "● Needs export";
            if (reason.IndexOf("output", StringComparison.OrdinalIgnoreCase) >= 0)
                return "▲ Output missing";
            if (reason.IndexOf("settings", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("scope", StringComparison.OrdinalIgnoreCase) >= 0)
                return "▲ Settings changed";
            if (reason.IndexOf("ACC model tip version", StringComparison.OrdinalIgnoreCase) >= 0)
                return "▲ ACC updated" + modified;
            if (reason.IndexOf("modified after", StringComparison.OrdinalIgnoreCase) >= 0)
                return "▲ Update NWC" + modified;

            return "▲ Needs export" + modified;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class VerifiedFreshnessToolTipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ModelExportJob job) || App.ConfigManager == null)
                return "Freshness could not be evaluated.";

            FreshnessEvaluation evaluation = FreshnessEvaluator.Evaluate(job, App.ConfigManager.CurrentSettings);
            return evaluation.Reason;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
