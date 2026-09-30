using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using ScheduledNwcExporter.Application;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Core;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Operations;
using ScheduledNwcExporter.Revit;

namespace ScheduledNwcExporter.UI.ViewModels
{
    public enum TestStatus
    {
        Pending,
        Running,
        Success,
        Warning,
        Error
    }

    public class DiagnosticTest : BindableBase
    {
        private string _title = string.Empty;
        public string Title { get => _title; set => SetProperty(ref _title, value); }

        private TestStatus _status = TestStatus.Pending;
        public TestStatus Status { get => _status; set { if (SetProperty(ref _status, value)) OnPropertyChanged(nameof(StatusIcon)); } }

        private string _details = string.Empty;
        public string Details { get => _details; set => SetProperty(ref _details, value); }

        public string StatusIcon => Status switch
        {
            TestStatus.Pending => "⚪",
            TestStatus.Running => "🔵",
            TestStatus.Success => "✅",
            TestStatus.Warning => "⚠️",
            TestStatus.Error => "❌",
            _ => "❓"
        };
    }

    public class DiagnosticsViewModel : BindableBase
    {
        private readonly ILogger _logger;
        private readonly AppSettings _settings;

        public ObservableCollection<DiagnosticTest> Tests { get; } = new ObservableCollection<DiagnosticTest>();

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }

        public ICommand RunTestsCommand { get; }

        public DiagnosticsViewModel(ILogger logger, AppSettings settings)
        {
            _logger = logger;
            _settings = settings;
            RunTestsCommand = new RelayCommand(_ => RunAllTests());
            InitializeTests();
        }

        private void InitializeTests()
        {
            Tests.Clear();
            Tests.Add(new DiagnosticTest { Title = "Add-in / Revit", Details = "Inspecting add-in and Revit API versions." });
            Tests.Add(new DiagnosticTest { Title = "NWC Exporter", Details = "Checking Navisworks exporter availability." });
            Tests.Add(new DiagnosticTest { Title = "Cloud Authentication", Details = "Checking the current Autodesk sign-in session." });
            Tests.Add(new DiagnosticTest { Title = "Configuration Schema", Details = "Checking configuration schema and persistence path." });
            Tests.Add(new DiagnosticTest { Title = "Queue / Scheduler", Details = "Inspecting current automation state." });
            Tests.Add(new DiagnosticTest { Title = "Logs / Reports", Details = "Checking operational storage paths." });

            foreach (var job in _settings.Jobs.Where(j => j.IsEnabled))
            {
                string modelLabel = job.IsCloud ? job.DisplaySourcePath : Path.GetFileName(job.SourceModelPath);
                Tests.Add(new DiagnosticTest
                {
                    Title = $"Model: {modelLabel}",
                    Details = "Verifying source and output accessibility."
                });
            }
        }

        private void RunAllTests()
        {
            if (IsRunning) return;
            IsRunning = true;

            try
            {
                int index = 0;

                DiagnosticTest environment = Tests[index++];
                environment.Status = TestStatus.Running;
                Version addinVersion = Assembly.GetExecutingAssembly().GetName().Version;
                Version revitApiVersion = typeof(Autodesk.Revit.DB.Element).Assembly.GetName().Version;
                environment.Details = $"Add-in {addinVersion}; Revit API {revitApiVersion}; .NET {Environment.Version}; {Environment.OSVersion}.";
                environment.Status = TestStatus.Success;

                DiagnosticTest exporter = Tests[index++];
                exporter.Status = TestStatus.Running;
                var exporterService = new NwcExporterService(_logger);
                if (exporterService.IsExporterAvailable())
                {
                    exporter.Details = "Navisworks NWC Exporter is installed and available in this Revit session.";
                    exporter.Status = TestStatus.Success;
                }
                else
                {
                    exporter.Details = "Navisworks NWC Exporter is not available. Batch export cannot proceed.";
                    exporter.Status = TestStatus.Error;
                }

                DiagnosticTest auth = Tests[index++];
                auth.Status = TestStatus.Running;
                string token = CloudAuthenticationService.GetAccessToken();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    auth.Details = "Autodesk session token is available for ACC metadata/model access.";
                    auth.Status = TestStatus.Success;
                }
                else
                {
                    auth.Details = "No Autodesk session token is available. Local jobs remain usable; ACC jobs require sign-in.";
                    auth.Status = TestStatus.Warning;
                }

                DiagnosticTest config = Tests[index++];
                config.Status = TestStatus.Running;
                int schemaVersion = ConfigurationSchemaService.ReadVersion();
                config.Details = $"Schema v{schemaVersion}/{ConfigurationSchemaService.CurrentVersion}; {ConfigurationSchemaService.ConfigPath}";
                config.Status = schemaVersion >= ConfigurationSchemaService.CurrentVersion ? TestStatus.Success : TestStatus.Warning;

                DiagnosticTest automation = Tests[index++];
                automation.Status = TestStatus.Running;
                int activeSlots = _settings.Scheduler.Slots?.Count(slot => slot.IsEnabled) ?? 0;
                string queueState = App.QueueHandler?.IsSessionRunning == true ? "Running" : "Idle";
                automation.Details = $"Queue: {queueState}; Scheduler: {(_settings.Scheduler.IsSchedulerEnabled ? "Enabled" : "Disabled")}; active slots: {activeSlots}.";
                automation.Status = TestStatus.Success;

                DiagnosticTest storage = Tests[index++];
                storage.Status = TestStatus.Running;
                bool logDirectoryOk = Directory.Exists(_logger.LogDirectory);
                try { Directory.CreateDirectory(SessionHistoryService.ReportsDirectory); } catch { }
                bool reportsOk = Directory.Exists(SessionHistoryService.ReportsDirectory);
                storage.Details = $"Log: {_logger.LogFilePath} | History: {SessionHistoryService.HistoryFilePath} | Reports: {SessionHistoryService.ReportsDirectory}";
                storage.Status = logDirectoryOk && reportsOk ? TestStatus.Success : TestStatus.Error;

                var activeJobs = _settings.Jobs.Where(j => j.IsEnabled).ToList();
                foreach (var job in activeJobs)
                {
                    if (index >= Tests.Count) break;
                    DiagnosticTest jobTest = Tests[index++];
                    jobTest.Status = TestStatus.Running;

                    bool sourceOk;
                    string sourceDetails;
                    if (job.IsCloud)
                    {
                        sourceOk = !string.IsNullOrWhiteSpace(token);
                        sourceDetails = sourceOk
                            ? $"ACC session available for {job.DisplaySourcePath}."
                            : $"ACC source requires Autodesk sign-in: {job.DisplaySourcePath}.";
                    }
                    else
                    {
                        sourceOk = File.Exists(job.SourceModelPath);
                        sourceDetails = sourceOk ? "Local source exists." : "Local source NOT found.";
                    }

                    bool outputOk = false;
                    try
                    {
                        Directory.CreateDirectory(job.OutputDirectory);
                        string testFile = Path.Combine(job.OutputDirectory, ".hatco_diagnostic_write_" + Guid.NewGuid().ToString("N") + ".tmp");
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                        outputOk = true;
                    }
                    catch (Exception ex)
                    {
                        sourceDetails += $" | Output error: {ex.Message}";
                    }

                    jobTest.Details = $"{sourceDetails} | Output: {(outputOk ? "Writable" : "Inaccessible")}";
                    jobTest.Status = sourceOk && outputOk ? TestStatus.Success : TestStatus.Error;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Diagnostics", $"Unexpected error during diagnostics: {ex.Message}", string.Empty, "Diagnostics", ex);
            }
            finally
            {
                IsRunning = false;
            }
        }
    }
}
