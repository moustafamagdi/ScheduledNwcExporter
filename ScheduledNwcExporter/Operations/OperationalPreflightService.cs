using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Core;

namespace ScheduledNwcExporter.Operations
{
    public enum PreflightSeverity
    {
        Warning,
        Error
    }

    public sealed class PreflightIssue
    {
        public PreflightSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class PreflightResult
    {
        public List<PreflightIssue> Issues { get; } = new List<PreflightIssue>();
        public bool HasErrors => Issues.Any(issue => issue.Severity == PreflightSeverity.Error);
        public bool HasWarnings => Issues.Any(issue => issue.Severity == PreflightSeverity.Warning);

        public string ToDisplayText()
        {
            if (Issues.Count == 0) return "No operational preflight issues found.";
            return string.Join(Environment.NewLine, Issues.Select(issue =>
                (issue.Severity == PreflightSeverity.Error ? "ERROR: " : "WARNING: ") + issue.Message));
        }
    }

    public static class OperationalPreflightService
    {
        private const long LowDiskWarningBytes = 2L * 1024L * 1024L * 1024L;

        public static PreflightResult Evaluate(IEnumerable<ModelExportJob> jobs)
        {
            var result = new PreflightResult();
            var jobList = (jobs ?? Enumerable.Empty<ModelExportJob>()).Where(job => job != null).ToList();

            if (jobList.Count == 0)
            {
                result.Issues.Add(new PreflightIssue { Severity = PreflightSeverity.Error, Message = "No enabled export jobs were supplied." });
                return result;
            }

            bool hasCloudJobs = jobList.Any(job => job.IsCloud);
            if (hasCloudJobs && string.IsNullOrWhiteSpace(CloudAuthenticationService.GetAccessToken()))
            {
                result.Issues.Add(new PreflightIssue
                {
                    Severity = PreflightSeverity.Error,
                    Message = "One or more ACC jobs are selected but no active Autodesk sign-in token is available."
                });
            }

            foreach (ModelExportJob job in jobList)
            {
                if (!job.IsCloud && !File.Exists(job.SourceModelPath))
                {
                    result.Issues.Add(new PreflightIssue
                    {
                        Severity = PreflightSeverity.Error,
                        Message = $"Source model not found: {job.SourceModelPath}"
                    });
                }

                if (string.IsNullOrWhiteSpace(job.OutputDirectory))
                {
                    result.Issues.Add(new PreflightIssue
                    {
                        Severity = PreflightSeverity.Error,
                        Message = $"Output folder is not configured for {job.DisplaySourcePath}."
                    });
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(job.OutputDirectory);
                    string probe = Path.Combine(job.OutputDirectory, ".hatco_nwc_write_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
                    File.WriteAllText(probe, "probe");
                    File.Delete(probe);
                }
                catch (Exception ex)
                {
                    result.Issues.Add(new PreflightIssue
                    {
                        Severity = PreflightSeverity.Error,
                        Message = $"Output folder is not writable for {job.DisplaySourcePath}: {job.OutputDirectory} ({ex.Message})"
                    });
                    continue;
                }

                TryAddDiskWarning(job.OutputDirectory, result);
            }

            return result;
        }

        private static void TryAddDiskWarning(string outputDirectory, PreflightResult result)
        {
            try
            {
                if (outputDirectory.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase)) return;
                string root = Path.GetPathRoot(Path.GetFullPath(outputDirectory));
                if (string.IsNullOrWhiteSpace(root)) return;

                var drive = new DriveInfo(root);
                if (!drive.IsReady) return;
                if (drive.AvailableFreeSpace < LowDiskWarningBytes)
                {
                    double freeGb = drive.AvailableFreeSpace / (1024d * 1024d * 1024d);
                    result.Issues.Add(new PreflightIssue
                    {
                        Severity = PreflightSeverity.Warning,
                        Message = $"Low free space on {drive.Name}: {freeGb:F1} GB available."
                    });
                }
            }
            catch
            {
                // Disk-space inspection is advisory only; access checks above remain authoritative.
            }
        }
    }
}
