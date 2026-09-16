using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Operations;
using ScheduledNwcExporter.Queue;
using ScheduledNwcExporter.Reliability;

namespace ScheduledNwcExporter.Revit.ExternalEvents
{
    public sealed class ExportSessionProgress : EventArgs
    {
        public int CompletedJobs { get; set; }
        public int TotalJobs { get; set; }
        public string ModelName { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public int PercentComplete => TotalJobs == 0 ? 0 : (int)Math.Round(100d * CompletedJobs / TotalJobs);
    }

    public enum SessionTriggerSource { Manual, Scheduler }

    public sealed class ExportSessionJobResult
    {
        public string JobId { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string FreshnessBefore { get; set; } = string.Empty;
        public string FreshnessAfter { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public sealed class ExportSessionSummary : EventArgs
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
        public int TotalModels { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int Cancelled { get; set; }
        public TimeSpan Duration { get; set; }
        public string SessionError { get; set; } = string.Empty;
        public string ReportPath { get; set; } = string.Empty;
        public List<string> FailedModels { get; } = new List<string>();
        public List<ExportSessionJobResult> Results { get; } = new List<ExportSessionJobResult>();
        public SessionTriggerSource TriggerSource { get; set; } = SessionTriggerSource.Manual;
    }

    /// <summary>
    /// Revit-owned queue dispatcher for the modeless WPF interface. Revit API work happens only
    /// inside Execute. Retry waiting happens on DispatcherTimer between ExternalEvent executions.
    /// </summary>
    public sealed class ExportQueueExternalEventHandler : IExternalEventHandler
    {
        private readonly ILogger _logger;
        private readonly AppSettings _settings;
        private readonly ConfigurationManager? _configurationManager;
        private readonly Dispatcher _uiDispatcher;
        private readonly List<ModelExportJob> _jobs = new List<ModelExportJob>();
        private readonly ExportSessionSummary _summary = new ExportSessionSummary();
        private readonly Dictionary<string, string> _freshnessBefore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private ExternalEvent? _externalEvent;
        private DispatcherTimer? _retryTimer;
        private int _nextJobIndex;
        private int _currentAttempt = 1;
        private DateTime _sessionStartedAt;
        private TimeSpan _currentJobDuration = TimeSpan.Zero;
        private bool _cancelRequested;
        private bool _exporterValidated;

        public bool IsSessionRunning { get; private set; }
        public string LastStartIssue { get; private set; } = string.Empty;

        public event EventHandler<ExportSessionProgress>? ProgressChanged;
        public event EventHandler<ExportSessionSummary>? SessionCompleted;

        public ExportQueueExternalEventHandler(ILogger logger, AppSettings settings, Dispatcher uiDispatcher, ConfigurationManager? configurationManager = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _configurationManager = configurationManager;
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        }

        public void AttachExternalEvent(ExternalEvent externalEvent)
        {
            _externalEvent = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));
        }

        public bool Start(IEnumerable<ModelExportJob> jobs, SessionTriggerSource triggerSource = SessionTriggerSource.Manual)
        {
            LastStartIssue = string.Empty;
            if (IsSessionRunning || _externalEvent == null)
            {
                LastStartIssue = IsSessionRunning ? "Another export session is already running." : "The Revit external event is unavailable.";
                return false;
            }

            _jobs.Clear();
            _jobs.AddRange(jobs ?? Enumerable.Empty<ModelExportJob>());
            if (_jobs.Count == 0)
            {
                LastStartIssue = "No enabled export jobs were supplied.";
                return false;
            }

            PreflightResult preflight = OperationalPreflightService.Evaluate(_jobs);
            foreach (PreflightIssue issue in preflight.Issues)
            {
                if (issue.Severity == PreflightSeverity.Error)
                    _logger.Error("Preflight", issue.Message, string.Empty, "OperationalPreflight");
                else
                    _logger.Warning("Preflight", issue.Message, string.Empty, "OperationalPreflight");
            }

            if (preflight.HasErrors)
            {
                LastStartIssue = preflight.ToDisplayText();
                if (triggerSource == SessionTriggerSource.Manual)
                {
                    MessageBox.Show(LastStartIssue, "Hatco NWC Exporter - Preflight blocked", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                _jobs.Clear();
                return false;
            }

            if (preflight.HasWarnings && triggerSource == SessionTriggerSource.Manual)
            {
                MessageBox.Show(preflight.ToDisplayText(), "Hatco NWC Exporter - Preflight warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            StopRetryTimer();
            _nextJobIndex = 0;
            _currentAttempt = 1;
            _currentJobDuration = TimeSpan.Zero;
            _cancelRequested = false;
            _exporterValidated = false;
            _sessionStartedAt = DateTime.Now;
            IsSessionRunning = true;

            _summary.SessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _summary.StartedAt = _sessionStartedAt;
            _summary.EndedAt = default(DateTime);
            _summary.TotalModels = _jobs.Count;
            _summary.Successful = 0;
            _summary.Failed = 0;
            _summary.Skipped = 0;
            _summary.Cancelled = 0;
            _summary.Duration = TimeSpan.Zero;
            _summary.SessionError = string.Empty;
            _summary.ReportPath = string.Empty;
            _summary.FailedModels.Clear();
            _summary.Results.Clear();
            _summary.TriggerSource = triggerSource;

            _freshnessBefore.Clear();
            foreach (ModelExportJob job in _jobs)
                _freshnessBefore[GetJobKey(job)] = FormatFreshness(FreshnessEvaluator.Evaluate(job, _settings));

            _logger.SetSessionContext(_summary.SessionId);
            _logger.Info("Scheduler", $"Export session queued. Total models: {_summary.TotalModels}; trigger: {triggerSource}.");
            string firstModelName = GetSafeModelName(_jobs[0].SourceModelPath);
            PublishProgress(firstModelName, "Waiting for Revit to become idle to begin processing…");
            _externalEvent.Raise();
            return true;
        }

        public void RequestCancellation()
        {
            if (!IsSessionRunning) return;

            _cancelRequested = true;
            _logger.Warning("Scheduler", "Cancellation requested. The queue will stop at the next safe boundary.", string.Empty, "Cancellation");
            if (_retryTimer != null)
            {
                StopRetryTimer();
                _externalEvent?.Raise();
            }
        }

        public void Execute(UIApplication application)
        {
            if (!IsSessionRunning) return;

            try
            {
                if (_nextJobIndex >= _jobs.Count)
                {
                    CompleteSession();
                    return;
                }

                ModelExportJob currentJob = _jobs[_nextJobIndex];
                string modelName = GetSafeModelName(currentJob.SourceModelPath);
                _logger.SetJobContext(currentJob.Id);
                var processor = new JobProcessor(application.Application, _settings, _logger);

                if (_cancelRequested)
                {
                    var cancelled = new JobExecutionResult
                    {
                        ModelName = modelName,
                        Cancelled = true,
                        ErrorMessage = "Cancelled before beginning the next Revit operation.",
                        Duration = _currentJobDuration
                    };
                    processor.FinalizeJob(currentJob, cancelled, PublishProgress);
                    AccumulateResult(currentJob, cancelled);
                    _nextJobIndex++;
                    _logger.ClearJobContext();
                    CompleteSession();
                    return;
                }

                PublishProgress(modelName, $"Revit context acquired. Model {_nextJobIndex + 1} of {_jobs.Count}; attempt {_currentAttempt}…");

                if (!_exporterValidated)
                {
                    var exporter = new NwcExporterService(_logger);
                    if (!exporter.IsExporterAvailable())
                    {
                        _summary.SessionError = "The compatible Navisworks NWC Exporter is not available in this Revit session.";
                        _logger.Error("Scheduler", _summary.SessionError, string.Empty, "Preflight");
                        CompleteSession();
                        return;
                    }
                    _exporterValidated = true;
                }

                JobExecutionResult jobResult = processor.ProcessSingleAttempt(
                    currentJob,
                    () => _cancelRequested,
                    _currentAttempt,
                    PublishProgress);

                _currentJobDuration += jobResult.Duration;
                int maximumAttempts = Math.Max(1, currentJob.RetryCount + 1);
                if (!jobResult.Succeeded &&
                    !jobResult.Skipped &&
                    !jobResult.Cancelled &&
                    jobResult.Retryable &&
                    _currentAttempt < maximumAttempts)
                {
                    _currentAttempt++;
                    ScheduleRetry(currentJob, modelName, maximumAttempts);
                    return;
                }

                jobResult.Duration = _currentJobDuration;
                processor.FinalizeJob(currentJob, jobResult, PublishProgress);
                AccumulateResult(currentJob, jobResult);

                _nextJobIndex++;
                _currentAttempt = 1;
                _currentJobDuration = TimeSpan.Zero;
                _logger.ClearJobContext();
                PublishProgress(modelName, currentJob.Status.ToString());

                if (_cancelRequested || _nextJobIndex >= _jobs.Count)
                {
                    CompleteSession();
                    return;
                }

                _uiDispatcher.BeginInvoke(new Action(RaiseNextJob), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                _summary.SessionError = $"The export queue stopped unexpectedly: {ex.Message}";
                _logger.Fatal("Scheduler", _summary.SessionError, string.Empty, "ExternalEvent", ex);
                _logger.ClearJobContext();
                CompleteSession();
            }
        }

        public string GetName() => "Scheduled NWC Export Queue External Event";

        private void ScheduleRetry(ModelExportJob job, string modelName, int maximumAttempts)
        {
            StopRetryTimer();
            int delaySeconds = Math.Max(0, job.RetryDelaySeconds);
            string stage = delaySeconds > 0
                ? $"Retry {_currentAttempt}/{maximumAttempts} in {delaySeconds}s"
                : $"Retry {_currentAttempt}/{maximumAttempts}";

            job.Status = JobStatus.Retrying;
            job.CurrentStage = stage;
            job.ProgressPercentage = 0;
            PublishProgress(modelName, stage);
            _logger.Warning("Job", $"Attempt {_currentAttempt - 1} failed. {stage}.", modelName, "Retrying");

            _retryTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, _uiDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, delaySeconds * 1000))
            };
            _retryTimer.Tick += RetryTimer_Tick;
            _retryTimer.Start();
        }

        private void RetryTimer_Tick(object? sender, EventArgs e)
        {
            StopRetryTimer();
            if (IsSessionRunning && _externalEvent != null)
                _externalEvent.Raise();
        }

        private void StopRetryTimer()
        {
            if (_retryTimer == null) return;
            _retryTimer.Stop();
            _retryTimer.Tick -= RetryTimer_Tick;
            _retryTimer = null;
        }

        private void AccumulateResult(ModelExportJob job, JobExecutionResult result)
        {
            string status;
            if (result.Succeeded)
            {
                _summary.Successful++;
                status = "Success";
            }
            else if (result.Skipped)
            {
                _summary.Skipped++;
                status = result.SkippedExisting ? "SkippedExisting" : "Skipped";
            }
            else if (result.Cancelled)
            {
                _summary.Cancelled++;
                status = "Cancelled";
            }
            else
            {
                _summary.Failed++;
                status = "Failed";
                _summary.FailedModels.Add($"{result.ModelName}: {result.ErrorMessage}");
            }

            string key = GetJobKey(job);
            _freshnessBefore.TryGetValue(key, out string before);
            FreshnessEvaluation afterEvaluation = FreshnessEvaluator.Evaluate(job, _settings);
            _summary.Results.Add(new ExportSessionJobResult
            {
                JobId = job.Id,
                ModelName = result.ModelName,
                Status = status,
                Duration = result.Duration,
                OutputPath = string.IsNullOrWhiteSpace(result.OutputPath) ? job.FullOutputPath : result.OutputPath,
                FreshnessBefore = before ?? string.Empty,
                FreshnessAfter = FormatFreshness(afterEvaluation),
                ErrorMessage = result.ErrorMessage
            });
        }

        private void RaiseNextJob()
        {
            if (IsSessionRunning && !_cancelRequested && _externalEvent != null)
                _externalEvent.Raise();
        }

        private void PublishProgress(string modelName, string stage)
        {
            _uiDispatcher.BeginInvoke(new Action(() =>
            {
                ProgressChanged?.Invoke(this, new ExportSessionProgress
                {
                    CompletedJobs = _nextJobIndex,
                    TotalJobs = _jobs.Count,
                    ModelName = modelName,
                    Stage = stage
                });
            }));
        }

        private void CompleteSession()
        {
            if (!IsSessionRunning) return;

            StopRetryTimer();
            IsSessionRunning = false;
            _summary.EndedAt = DateTime.Now;
            _summary.Duration = _summary.EndedAt - _sessionStartedAt;
            _configurationManager?.SaveConfiguration();

            try
            {
                _summary.ReportPath = SessionHistoryService.WriteCsvReport(_summary);
                SessionHistoryService.Record(_summary);
                _logger.Info("Reporting", $"Session report created: {_summary.ReportPath}");
            }
            catch (Exception ex)
            {
                _logger.Warning("Reporting", $"Could not persist session history/report: {ex.Message}", string.Empty, "SessionReport", ex);
            }

            PublishProgress(string.Empty, string.IsNullOrWhiteSpace(_summary.SessionError) ? "Queue completed." : _summary.SessionError);
            _logger.Info(
                "Scheduler",
                $"Export session finished. Successful: {_summary.Successful}; Failed: {_summary.Failed}; Skipped: {_summary.Skipped}; Cancelled: {_summary.Cancelled}; Duration: {_summary.Duration:hh\\:mm\\:ss}.");

            SessionCompleted?.Invoke(this, _summary);
            _logger.ClearJobContext();
            _logger.ClearSessionContext();
        }

        private static string FormatFreshness(FreshnessEvaluation evaluation)
        {
            return evaluation.NeedsExport ? "Needs Export: " + evaluation.Reason : "Current: " + evaluation.Reason;
        }

        private static string GetJobKey(ModelExportJob job)
        {
            return !string.IsNullOrWhiteSpace(job.Id) ? job.Id : job.SourceModelPath;
        }

        private string GetSafeModelName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "Unknown";
            if (path.StartsWith("acc://", StringComparison.OrdinalIgnoreCase))
            {
                string temp = path.Substring(6);
                string[] parts = temp.Split('|');
                return parts.Length > 0 ? parts[0] : temp;
            }
            return System.IO.Path.GetFileName(path);
        }
    }
}
