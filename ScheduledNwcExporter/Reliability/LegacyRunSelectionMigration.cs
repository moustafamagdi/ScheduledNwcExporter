using System;
using System.IO;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Reliability
{
    /// <summary>
    /// One-time migration for configs created by builds where the queue checkbox reused
    /// ModelExportJob.IsEnabled for smart freshness selection. Those builds could persist
    /// Current models as disabled, so after IsEnabled was redefined as persistent job
    /// eligibility the legacy values became indistinguishable from intentional disables.
    ///
    /// The safest migration for existing production queues is to restore real configured
    /// jobs to enabled once, then let the new transient IsSelectedForRun property control
    /// which jobs participate in a manual run. A marker prevents this from ever overriding
    /// intentional Job Enabled choices made after migration.
    /// </summary>
    internal static class LegacyRunSelectionMigration
    {
        private const string MarkerFileName = "run-selection-semantics-v2.migrated";

        public static void RunOnce(ConfigurationManager configManager, ILogger logger)
        {
            if (configManager == null || logger == null) return;

            string configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MoustafaMagdi",
                "ScheduledNwcExporter");
            string markerPath = Path.Combine(configDirectory, MarkerFileName);

            if (File.Exists(markerPath)) return;

            try
            {
                int restored = 0;
                if (configManager.CurrentSettings.Jobs != null)
                {
                    foreach (ModelExportJob job in configManager.CurrentSettings.Jobs)
                    {
                        if (job == null || IsSamplePlaceholder(job)) continue;

                        if (!job.IsEnabled)
                        {
                            job.IsEnabled = true;
                            restored++;
                        }
                    }
                }

                if (restored > 0 && !configManager.SaveConfiguration())
                {
                    logger.Warning(
                        "Config",
                        "Legacy run-selection migration could not persist restored job eligibility. The migration will be retried next launch.",
                        string.Empty,
                        "Migration");
                    return;
                }

                Directory.CreateDirectory(configDirectory);
                File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));

                logger.Info(
                    "Config",
                    $"Completed one-time run-selection migration. Restored persistent eligibility for {restored} configured job(s); future smart selection uses IsSelectedForRun only.",
                    string.Empty,
                    "Migration");
            }
            catch (Exception ex)
            {
                logger.Warning(
                    "Config",
                    $"Legacy run-selection migration could not complete: {ex.Message}",
                    string.Empty,
                    "Migration",
                    ex);
            }
        }

        private static bool IsSamplePlaceholder(ModelExportJob job)
        {
            return string.Equals(
                job.SourceModelPath,
                @"C:\Projects\SampleModel.rvt",
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
