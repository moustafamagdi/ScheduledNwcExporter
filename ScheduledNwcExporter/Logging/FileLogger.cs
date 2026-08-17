using System;
using System.IO;

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
        public TimeSpan Duration { get; set; }

        public override string ToString()
        {
            string timeStr = Timestamp.ToString("HH:mm:ss.fff");
            string modelInfo = string.IsNullOrEmpty(ModelName) ? "" : $" [Model: {ModelName}]";
            string stageInfo = string.IsNullOrEmpty(Stage) ? "" : $" [Stage: {Stage}]";
            string exInfo = Exception != null ? $" | Exception: {Exception.GetType().Name}: {Exception.Message}" : "";
            return $"[{timeStr}] [{Level,-7}] [{Category}]{modelInfo}{stageInfo} {Message}{exInfo}";
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
        string LogFilePath { get; }
        bool DebugMode { get; set; }
    }

    public class FileLogger : ILogger
    {
        private readonly string _logDirectory;
        private readonly object _lock = new object();
        public string LogFilePath { get; }
        public bool DebugMode { get; set; } = false;

        public FileLogger()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _logDirectory = Path.Combine(appData, "MoustafaMagdi", "ScheduledNwcExporter", "logs");
            Directory.CreateDirectory(_logDirectory);
            string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
            LogFilePath = Path.Combine(_logDirectory, fileName);
            Info("Logging", $"Log session initialized at {LogFilePath}");
        }

        public void Log(LogLevel level, string category, string message, string modelName = "", string stage = "", Exception? ex = null)
        {
            if (level == LogLevel.DEBUG && !DebugMode) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                ModelName = modelName,
                Stage = stage,
                Message = message,
                Exception = ex
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
                    // Fallback to console if file write fails
                }
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
