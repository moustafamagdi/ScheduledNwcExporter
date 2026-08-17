using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Input;

namespace ScheduledNwcExporter.UI.ViewModels
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
    }

    public class LogViewerViewModel : BindableBase
    {
        private readonly string _logFilePath;
        private ObservableCollection<LogEntry> _allLogs = new ObservableCollection<LogEntry>();
        
        public CollectionViewSource LogViewSource { get; }

        private string _levelFilter = "All";
        public string LevelFilter
        {
            get => _levelFilter;
            set { if (SetProperty(ref _levelFilter, value)) LogViewSource.View.Refresh(); }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) LogViewSource.View.Refresh(); }
        }

        public List<string> Levels { get; } = new List<string> { "All", "INFO", "SUCCESS", "WARNING", "ERROR", "FATAL", "DEBUG" };

        public ICommand RefreshCommand { get; }

        public LogViewerViewModel(string logFilePath)
        {
            _logFilePath = logFilePath;
            LogViewSource = new CollectionViewSource { Source = _allLogs };
            LogViewSource.Filter += ApplyFilter;

            RefreshCommand = new RelayCommand(_ => LoadLogs());
            LoadLogs();
        }

        private void LoadLogs()
        {
            if (!File.Exists(_logFilePath)) return;

            try
            {
                _allLogs.Clear();
                var lines = File.ReadAllLines(_logFilePath);
                
                // Regex for: [16:25:57.961] [INFO   ] [Logging] Message...
                // Some logs have Model/Stage: [Model: X] [Stage: Y]
                var logRegex = new Regex(@"^\[(?<time>[^\]]+)\]\s+\[(?<level>[^\]]+)\]\s+\[(?<cat>[^\]]+)\]\s+(?<msg>.*)$");
                var modelRegex = new Regex(@"\[Model:\s*(?<model>[^\]]+)\]");
                var stageRegex = new Regex(@"\[Stage:\s*(?<stage>[^\]]+)\]");

                foreach (var line in lines)
                {
                    var match = logRegex.Match(line);
                    if (match.Success)
                    {
                        var entry = new LogEntry
                        {
                            Level = match.Groups["level"].Value.Trim(),
                            Category = match.Groups["cat"].Value.Trim(),
                            Message = match.Groups["msg"].Value.Trim()
                        };

                        if (DateTime.TryParse(match.Groups["time"].Value, out DateTime ts))
                        {
                            entry.Timestamp = ts;
                        }

                        var modelMatch = modelRegex.Match(entry.Message);
                        if (modelMatch.Success) entry.Model = modelMatch.Groups["model"].Value;

                        var stageMatch = stageRegex.Match(entry.Message);
                        if (stageMatch.Success) entry.Stage = stageMatch.Groups["stage"].Value;

                        _allLogs.Add(entry);
                    }
                }
            }
            catch { /* Ignore read errors */ }
        }

        private void ApplyFilter(object sender, FilterEventArgs e)
        {
            if (e.Item is LogEntry entry)
            {
                bool levelMatch = LevelFilter == "All" || entry.Level.Equals(LevelFilter, StringComparison.OrdinalIgnoreCase);
                bool searchMatch = string.IsNullOrEmpty(SearchText) || 
                                   entry.Message.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   entry.Model.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   entry.Stage.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;

                e.Accepted = levelMatch && searchMatch;
            }
        }
    }
}
