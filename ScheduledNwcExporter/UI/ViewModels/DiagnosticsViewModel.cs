using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Core;
using ScheduledNwcExporter.Logging;
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
            Tests.Add(new DiagnosticTest { Title = "Revit Environment", Details = "Verifying Revit version and .NET framework." });
            Tests.Add(new DiagnosticTest { Title = "NWC Exporter Plugin", Details = "Checking if Navisworks Exporter is installed." });
            Tests.Add(new DiagnosticTest { Title = "Cloud Authentication", Details = "Verifying APS (Forge) access token via SSONET." });
            Tests.Add(new DiagnosticTest { Title = "Configuration Access", Details = "Verifying read/write access to settings and logs." });
            
            foreach (var job in _settings.Jobs.Where(j => j.IsEnabled))
            {
                Tests.Add(new DiagnosticTest { Title = $"Model: {Path.GetFileName(job.SourceModelPath)}", Details = "Verifying source and output accessibility." });
            }
        }

        private async void RunAllTests()
        {
            if (IsRunning) return;
            IsRunning = true;

            try
            {
                // 1. Revit Environment
                var revitTest = Tests[0];
                revitTest.Status = TestStatus.Running;
                revitTest.Details = $"Revit 2024 Target. Running on .NET {Environment.Version}. OS: {Environment.OSVersion}";
                revitTest.Status = TestStatus.Success;

                // 2. NWC Exporter
                var exporterTest = Tests[1];
                exporterTest.Status = TestStatus.Running;
                var exporterService = new NwcExporterService(_logger);
                if (exporterService.IsExporterAvailable())
                {
                    exporterTest.Details = "Navisworks NWC Exporter is installed and available.";
                    exporterTest.Status = TestStatus.Success;
                }
                else
                {
                    exporterTest.Details = "Navisworks NWC Exporter NOT found. Batch export will fail.";
                    exporterTest.Status = TestStatus.Error;
                }

                // 3. Cloud Auth
                var authTest = Tests[2];
                authTest.Status = TestStatus.Running;
                string token = CloudAuthenticationService.GetAccessToken();
                if (!string.IsNullOrEmpty(token))
                {
                    authTest.Details = "Cloud authentication token retrieved successfully.";
                    authTest.Status = TestStatus.Success;
                }
                else
                {
                    authTest.Details = "Could not retrieve cloud token. Cloud models cannot be exported.";
                    authTest.Status = TestStatus.Warning;
                }

                // 4. Config & Logs
                var configTest = Tests[3];
                configTest.Status = TestStatus.Running;
                if (Directory.Exists(Path.GetDirectoryName(_logger.LogFilePath)))
                {
                    configTest.Details = $"Log directory accessible: {Path.GetDirectoryName(_logger.LogFilePath)}";
                    configTest.Status = TestStatus.Success;
                }
                else
                {
                    configTest.Details = "Log directory is missing or inaccessible.";
                    configTest.Status = TestStatus.Error;
                }

                // 5. Individual Jobs
                int testIdx = 4;
                var activeJobs = _settings.Jobs.Where(j => j.IsEnabled).ToList();
                foreach (var job in activeJobs)
                {
                    if (testIdx >= Tests.Count) break;
                    var jobTest = Tests[testIdx++];
                    jobTest.Status = TestStatus.Running;

                    bool sourceOk = true;
                    string sourceDetails = "";
                    if (job.IsCloud)
                    {
                        sourceOk = !string.IsNullOrEmpty(token);
                        sourceDetails = sourceOk ? "Cloud source reachable." : "Cloud source requires active login.";
                    }
                    else
                    {
                        sourceOk = File.Exists(job.SourceModelPath);
                        sourceDetails = sourceOk ? "Local file exists." : "Local file NOT found.";
                    }

                    bool outputOk = false;
                    try
                    {
                        if (!Directory.Exists(job.OutputDirectory))
                        {
                            Directory.CreateDirectory(job.OutputDirectory);
                        }
                        string testFile = Path.Combine(job.OutputDirectory, "hatco_write_test.tmp");
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                        outputOk = true;
                    }
                    catch (Exception ex)
                    {
                        sourceDetails += $" | Output Error: {ex.Message}";
                    }

                    jobTest.Details = $"{sourceDetails} | Output: {(outputOk ? "Writable" : "Inaccessible")}";
                    jobTest.Status = (sourceOk && outputOk) ? TestStatus.Success : TestStatus.Error;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Diagnostics", $"Unexpected error during self-test: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }
    }
}
