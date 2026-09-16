using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Queue;

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

    public sealed class ExportSessionSummary : EventArgs
    {
        public int TotalModels { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int Cancelled { get; set; }
        public TimeSpan Duration { get; set; }
        public string SessionError { get; set; } = string.Empty;
        public List<string> FailedModels { get; } = new List<string>();
        public SessionTriggerSource TriggerSource { get; set; } = SessionTriggerSource.Manual;
    }

    /// <summary>
    /// Revit-owned queue dispatcher for the modeless WPF interface. Revit API work happens only
    /// inside Execute. Retry waiting happens on DispatcherTimer between ExternalEvent executions,
    /// never through Thread.Sleep while Revit owns the API context.
    /// </summary>
    public sealed class ExportQueueExternalEventHandler : IExternalEventHandler
    {
        private readonly ILogger _logger;
        private readonly AppSettings _settings;
        private readonly ConfigurationManager? _configurationManager;
        private readonly Dispatcher _uiDispatcher;
        private readonly List<ModelExportJob> _jobs = new List<ModelExportJob>();
        private readonly ExportSessionSummary _summary = new ExportSessionSummary();

        private ExternalEvent? _externalEvent;
        private DispatcherTimer? _retryTimer;
        private int _nextJobIndex;
        private int _currentAttempt = 1;
        private DateTime _sessionStartedAt;
        private bool _cancelRequested;
        private bool _exporterValidated;

        public bool IsSessionRunning { get; private set; }

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
            if (IsSessionRunning || _externalEvent == null)
                return false;

            _jobs.Clear();
            _jobs.AddRange(jobs ?? Enumerable.Empty<ModelExportJob>());
            if (_jobs.Count == 0)
                return false;

            StopRetryTimer();
            _nextJobIndex = 0;
            _currentAttempt = 1;
            _cancelRequested = false;
            _exporterValidated = false;
            _sessionStartedAt = DateTime.Now;
            IsSessionRunning = true;

            _summary.TotalModels = _jobs.Count;
            _summary.Successful = 0;
            _summary.Failed = 0;
            _summary.Skipped = 0;
            _summary.Cancelled = 0;
            _summary.Duration = TimeSpan.Zero;
            _summary.SessionError = string.Empty;
            _summary.FailedModels.Clear();
            _summary.TriggerSource = triggerSource;

            _logger.Info("Scheduler", $"Export session queued. Total models: {_summary.TotalModels}.");
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

            // If we are currently waiting between retries, wake the ExternalEvent immediately so
            // cancellation does not have to wait for the retry delay to expire.
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
                var processor = new JobProcessor(application.Application, _settings, _logger);

                if (_cancelRequested)
                {
                    var cancelled = new JobExecutionResult
                    {
                        ModelName = modelName,
                        Cancelled = true,
                        ErrorMessage = "Cancelled before beginning the next Revit operation."
                    };
                    processor.FinalizeJob(currentJob, cancelled, PublishProgress);
                    _summary.Cancelled++;
                    _nextJobIndex++;
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

                processor.FinalizeJob(currentJob, jobResult, PublishProgress);
                AccumulateResult(jobResult);

                _nextJobIndex++;
                _currentAttempt = 1;
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
                CompleteSession();
            }
        }

        public string GetName()
        {
            return "Scheduled NWC Export Queue External Event";
        }

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

        private void AccumulateResult(JobExecutionResult result)
        {
            if (result.Succeeded)
            {
                _summary.Successful++;
            }
            else if (result.Skipped)
            {
                _summary.Skipped++;
            }
            else if (result.Cancelled)
            {
                _summary.Cancelled++;
            }
            else
            {
                _summary.Failed++;
                _summary.FailedModels.Add($"{result.ModelName}: {result.ErrorMessage}");
            }
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
            _summary.Duration = DateTime.Now - _sessionStartedAt;
            _configurationManager?.SaveConfiguration();
            PublishProgress(string.Empty, string.IsNullOrWhiteSpace(_summary.SessionError) ? "Queue completed." : _summary.SessionError);
            _logger.Info(
                "Scheduler",
                $"Export session finished. Successful: {_summary.Successful}; Failed: {_summary.Failed}; Skipped: {_summary.Skipped}; Cancelled: {_summary.Cancelled}; Duration: {_summary.Duration:hh\\:mm\\:ss}.");

            SessionCompleted?.Invoke(this, _summary);
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
