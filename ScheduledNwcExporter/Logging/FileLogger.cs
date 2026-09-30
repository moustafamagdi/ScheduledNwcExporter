using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ScheduledNwcExporter.Logging
{
    public enum LogLevel
    {
        DEBUG,
        INFO,
        SUCCESS,
        WARNING,
        ERROR,
        FATAL
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; } = LogLevel.INFO;
        public string Category { get; set; } = "General";
        public string ModelName { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string JobId { get; set; } = string.Empty;

        public override string ToString()
        {
            string timeStr = Timestamp.ToString("HH:mm:ss.fff");
            string sessionInfo = string.IsNullOrEmpty(SessionId) ? "" : $" [Session: {SessionId}]";
            string jobInfo = string.IsNullOrEmpty(JobId) ? "" : $" [Job: {JobId}]";
            string modelInfo = string.IsNullOrEmpty(ModelName) ? "" : $" [Model: {ModelName}]";
            string stageInfo = string.IsNullOrEmpty(Stage) ? "" : $" [Stage: {Stage}]";
            string exInfo = Exception != null ? $" | Exception: {Exception.GetType().Name}: {Exception.Message}" : "";
            return $"[{timeStr}] [{Level,-7}] [{Category}]{sessionInfo}{jobInfo}{modelInfo}{stageInfo} {Message}{exInfo}";
        }
    }

    public interface ILogger
    {
        void Log(LogLevel level, string category, string message, string modelName = "", string stage = "", Exception? ex = null);
        void Debug(string category, string message, string modelName = "", string stage = "");
        void Info(string category, string message, string modelName = "", string stage = "");
        void Success(string category, string message, string modelName = "", string stage = "");
        void Warning(string category, string message, string modelName = "", string stage = "", Exception? ex = null);
        void Error(string category, string message, string modelName = "", string stage = "", Exception? ex = null);
        void Fatal(string category, string message, string modelName = "", string stage = "", Exception? ex = null);
        void SetSessionContext(string sessionId);
        void ClearSessionContext();
        void SetJobContext(string jobId);
        void ClearJobContext();
        string LogFilePath { get; }
        string LogDirectory { get; }
        string CurrentSessionId { get; }
        string CurrentJobId { get; }
        bool DebugMode { get; set; }
    }

    public class FileLogger : ILogger
    {
        private const int RetentionDays = 30;
        private const int MaximumLogFiles = 100;
        private const long MaximumTotalLogBytes = 100L * 1024L * 1024L;

        private readonly object _lock = new object();
        private string _currentSessionId = string.Empty;
        private string _currentJobId = string.Empty;

        public string LogFilePath { get; }
        public string LogDirectory { get; }
        public string CurrentSessionId { get { lock (_lock) return _currentSessionId; } }
        public string CurrentJobId { get { lock (_lock) return _currentJobId; } }
        public bool DebugMode { get; set; } = false;

        public FileLogger()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            LogDirectory = Path.Combine(appData, "MoustafaMagdi", "ScheduledNwcExporter", "logs");
            Directory.CreateDirectory(LogDirectory);
            CleanupOldLogs();
            string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
            LogFilePath = Path.Combine(LogDirectory, fileName);
            Info("Logging", $"Log session initialized at {LogFilePath}");
        }

        public void SetSessionContext(string sessionId)
        {
            lock (_lock)
            {
                _currentSessionId = sessionId ?? string.Empty;
                _currentJobId = string.Empty;
            }
        }

        public void ClearSessionContext()
        {
            lock (_lock)
            {
                _currentSessionId = string.Empty;
                _currentJobId = string.Empty;
            }
        }

        public void SetJobContext(string jobId)
        {
            lock (_lock) _currentJobId = jobId ?? string.Empty;
        }

        public void ClearJobContext()
        {
            lock (_lock) _currentJobId = string.Empty;
        }

        public void Log(LogLevel level, string category, string message, string modelName = "", string stage = "", Exception? ex = null)
        {
            if (level == LogLevel.DEBUG && !DebugMode) return;

            string sessionId;
            string jobId;
            lock (_lock)
            {
                sessionId = _currentSessionId;
                jobId = _currentJobId;
            }

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                ModelName = modelName,
                Stage = stage,
                Message = message,
                Exception = ex,
                SessionId = sessionId,
                JobId = jobId
            };

            string line = entry.ToString();
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
                catch
                {
                    // Logging must never stop the Revit automation workflow.
                }
            }
        }

        private void CleanupOldLogs()
        {
            try
            {
                var files = new DirectoryInfo(LogDirectory)
                    .GetFiles("*.log")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToList();

                DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
                foreach (FileInfo file in files.Where(file => file.LastWriteTimeUtc < cutoff).ToList())
                {
                    TryDelete(file);
                    files.Remove(file);
                }

                foreach (FileInfo file in files.Skip(MaximumLogFiles).ToList())
                {
                    TryDelete(file);
                    files.Remove(file);
                }

                long totalBytes = files.Sum(file => SafeLength(file));
                foreach (FileInfo file in files.OrderBy(file => file.LastWriteTimeUtc).ToList())
                {
                    if (totalBytes <= MaximumTotalLogBytes) break;
                    long length = SafeLength(file);
                    if (TryDelete(file)) totalBytes -= length;
                }
            }
            catch
            {
                // Retention cleanup is best-effort only.
            }
        }

        private static long SafeLength(FileInfo file)
        {
            try { return file.Length; } catch { return 0; }
        }

        private static bool TryDelete(FileInfo file)
        {
            try
            {
                file.Delete();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Debug(string category, string message, string modelName = "", string stage = "") => Log(LogLevel.DEBUG, category, message, modelName, stage);
        public void Info(string category, string message, string modelName = "", string stage = "") => Log(LogLevel.INFO, category, message, modelName, stage);
        public void Success(string category, string message, string modelName = "", string stage = "") => Log(LogLevel.SUCCESS, category, message, modelName, stage);
        public void Warning(string category, string message, string modelName = "", string stage = "", Exception? ex = null) => Log(LogLevel.WARNING, category, message, modelName, stage, ex);
        public void Error(string category, string message, string modelName = "", string stage = "", Exception? ex = null) => Log(LogLevel.ERROR, category, message, modelName, stage, ex);
        public void Fatal(string category, string message, string modelName = "", string stage = "", Exception? ex = null) => Log(LogLevel.FATAL, category, message, modelName, stage, ex);
    }
}
