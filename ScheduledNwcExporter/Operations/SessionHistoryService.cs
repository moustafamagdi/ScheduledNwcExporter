using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using ScheduledNwcExporter.Revit.ExternalEvents;

namespace ScheduledNwcExporter.Operations
{
    public sealed class SessionJobHistoryRecord
    {
        public string JobId { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public string FreshnessBefore { get; set; } = string.Empty;
        public string FreshnessAfter { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public sealed class SessionHistoryRecord
    {
        public string SessionId { get; set; } = string.Empty;
        public string TriggerSource { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
        public string Duration { get; set; } = string.Empty;
        public int TotalModels { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int Cancelled { get; set; }
        public string SessionError { get; set; } = string.Empty;
        public string ReportPath { get; set; } = string.Empty;
        public List<SessionJobHistoryRecord> Jobs { get; set; } = new List<SessionJobHistoryRecord>();

        [JsonIgnore]
        public string StartedDisplay => StartedAt.ToString("dd MMM yyyy HH:mm");

        [JsonIgnore]
        public string ResultDisplay => Failed > 0 || !string.IsNullOrWhiteSpace(SessionError)
            ? $"Failed: {Failed} / {TotalModels}"
            : $"Success: {Successful} / {TotalModels}";
    }

    public static class SessionHistoryService
    {
        private const int MaximumHistoryRecords = 200;
        private const int ReportRetentionDays = 90;
        private static readonly object Sync = new object();
        private static readonly string RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoustafaMagdi",
            "ScheduledNwcExporter");

        public static string HistoryFilePath { get; } = Path.Combine(RootDirectory, "session-history.json");
        public static string ReportsDirectory { get; } = Path.Combine(RootDirectory, "reports");

        public static SessionHistoryRecord Record(ExportSessionSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));

            var record = new SessionHistoryRecord
            {
                SessionId = summary.SessionId,
                TriggerSource = summary.TriggerSource.ToString(),
                StartedAt = summary.StartedAt,
                EndedAt = summary.EndedAt,
                Duration = summary.Duration.ToString(@"hh\:mm\:ss"),
                TotalModels = summary.TotalModels,
                Successful = summary.Successful,
                Failed = summary.Failed,
                Skipped = summary.Skipped,
                Cancelled = summary.Cancelled,
                SessionError = summary.SessionError,
                ReportPath = summary.ReportPath,
                Jobs = summary.Results.Select(result => new SessionJobHistoryRecord
                {
                    JobId = result.JobId,
                    ModelName = result.ModelName,
                    Status = result.Status,
                    Duration = result.Duration.ToString(@"hh\:mm\:ss"),
                    OutputPath = result.OutputPath,
                    FreshnessBefore = result.FreshnessBefore,
                    FreshnessAfter = result.FreshnessAfter,
                    ErrorMessage = result.ErrorMessage
                }).ToList()
            };

            lock (Sync)
            {
                List<SessionHistoryRecord> records = LoadUnsafe();
                records.RemoveAll(item => string.Equals(item.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));
                records.Insert(0, record);
                if (records.Count > MaximumHistoryRecords)
                    records = records.Take(MaximumHistoryRecords).ToList();
                SaveUnsafe(records);
            }

            CleanupReports();
            return record;
        }

        public static IReadOnlyList<SessionHistoryRecord> GetRecent(int maximum = 100)
        {
            lock (Sync)
            {
                return LoadUnsafe()
                    .OrderByDescending(record => record.StartedAt)
                    .Take(Math.Max(1, maximum))
                    .ToList();
            }
        }

        public static SessionHistoryRecord? GetLatest()
        {
            return GetRecent(1).FirstOrDefault();
        }

        public static SessionHistoryRecord? GetLatestScheduled()
        {
            return GetRecent(MaximumHistoryRecords)
                .FirstOrDefault(record => string.Equals(record.TriggerSource, SessionTriggerSource.Scheduler.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        public static string WriteCsvReport(ExportSessionSummary summary)
        {
            Directory.CreateDirectory(ReportsDirectory);
            string safeSession = string.IsNullOrWhiteSpace(summary.SessionId) ? DateTime.Now.ToString("yyyyMMdd-HHmmss") : summary.SessionId;
            string path = Path.Combine(ReportsDirectory, $"NWC_Export_{safeSession}.csv");

            var builder = new StringBuilder();
            builder.AppendLine("SessionId,Trigger,Started,Ended,Model,Status,Duration,OutputPath,FreshnessBefore,FreshnessAfter,Error");
            foreach (ExportSessionJobResult result in summary.Results)
            {
                builder.Append(Csv(summary.SessionId)).Append(',')
                    .Append(Csv(summary.TriggerSource.ToString())).Append(',')
                    .Append(Csv(summary.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
                    .Append(Csv(summary.EndedAt.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
                    .Append(Csv(result.ModelName)).Append(',')
                    .Append(Csv(result.Status)).Append(',')
                    .Append(Csv(result.Duration.ToString(@"hh\:mm\:ss"))).Append(',')
                    .Append(Csv(result.OutputPath)).Append(',')
                    .Append(Csv(result.FreshnessBefore)).Append(',')
                    .Append(Csv(result.FreshnessAfter)).Append(',')
                    .Append(Csv(result.ErrorMessage))
                    .AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string Csv(string value)
        {
            string safe = value ?? string.Empty;
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }

        private static List<SessionHistoryRecord> LoadUnsafe()
        {
            try
            {
                if (!File.Exists(HistoryFilePath)) return new List<SessionHistoryRecord>();
                string json = File.ReadAllText(HistoryFilePath);
                return JsonConvert.DeserializeObject<List<SessionHistoryRecord>>(json) ?? new List<SessionHistoryRecord>();
            }
            catch
            {
                return new List<SessionHistoryRecord>();
            }
        }

        private static void SaveUnsafe(List<SessionHistoryRecord> records)
        {
            Directory.CreateDirectory(RootDirectory);
            string tempPath = HistoryFilePath + ".tmp";
            string backupPath = HistoryFilePath + ".bak";
            string json = JsonConvert.SerializeObject(records, Formatting.Indented);

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(HistoryFilePath))
            {
                try
                {
                    File.Replace(tempPath, HistoryFilePath, backupPath, true);
                }
                catch
                {
                    File.Copy(tempPath, HistoryFilePath, true);
                    File.Delete(tempPath);
                }
            }
            else
            {
                File.Move(tempPath, HistoryFilePath);
            }
        }

        private static void CleanupReports()
        {
            try
            {
                Directory.CreateDirectory(ReportsDirectory);
                DateTime cutoff = DateTime.UtcNow.AddDays(-ReportRetentionDays);
                foreach (FileInfo file in new DirectoryInfo(ReportsDirectory).GetFiles("NWC_Export_*.csv"))
                {
                    if (file.LastWriteTimeUtc < cutoff)
                    {
                        try { file.Delete(); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
