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
    }

    /// <summary>
    /// Revit-owned queue dispatcher for the modeless WPF interface. Every Revit API call occurs
    /// only inside Execute, which Revit invokes after ExternalEvent.Raise().
    /// </summary>
    public sealed class ExportQueueExternalEventHandler : IExternalEventHandler
    {
        private readonly ILogger _logger;
        private readonly AppSettings _settings;
        private readonly Dispatcher _uiDispatcher;
        private readonly List<ModelExportJob> _jobs = new List<ModelExportJob>();
        private readonly ExportSessionSummary _summary = new ExportSessionSummary();

        private ExternalEvent? _externalEvent;
        private int _nextJobIndex;
        private DateTime _sessionStartedAt;
        private bool _cancelRequested;
        private bool _exporterValidated;

        public bool IsSessionRunning { get; private set; }

        public event EventHandler<ExportSessionProgress>? ProgressChanged;
        public event EventHandler<ExportSessionSummary>? SessionCompleted;

        public ExportQueueExternalEventHandler(ILogger logger, AppSettings settings, Dispatcher uiDispatcher)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        }

        public void AttachExternalEvent(ExternalEvent externalEvent)
        {
            _externalEvent = externalEvent ?? throw new ArgumentNullException(nameof(externalEvent));
        }

        /// <summary>
        /// Starts a queue by raising the Revit-owned external event. No Revit API access happens in this method.
        /// </summary>
        public bool Start(IEnumerable<ModelExportJob> jobs)
        {
            if (IsSessionRunning || _externalEvent == null)
            {
                return false;
            }

            _jobs.Clear();
            _jobs.AddRange(jobs ?? Enumerable.Empty<ModelExportJob>());
            _nextJobIndex = 0;
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

            _logger.Info("Scheduler", $"Export session queued. Total models: {_summary.TotalModels}.");
            
            string firstModelName = _jobs.Count > 0 ? GetSafeModelName(_jobs[0].SourceModelPath) : string.Empty;
            PublishProgress(firstModelName, "Waiting for Revit to become idle to begin processing…");
            
            _externalEvent.Raise();
            return true;
        }

        /// <summary>
        /// Requests cooperative cancellation. The active Revit operation finishes at its next safe boundary.
        /// </summary>
        public void RequestCancellation()
        {
            if (!IsSessionRunning) return;

            _cancelRequested = true;
            _logger.Warning("Scheduler", "Cancellation requested. The queue will stop before the next model begins.", string.Empty, "Cancellation");
        }

        public void Execute(UIApplication application)
        {
            if (!IsSessionRunning)
            {
                return;
            }

            try
            {
                // Immediately update UI to show we have entered the Revit API context
                ModelExportJob currentJob = _jobs[_nextJobIndex];
                string modelName = GetSafeModelName(currentJob.SourceModelPath);
                PublishProgress(modelName, $"Revit context acquired. Starting model {_nextJobIndex + 1} of {_jobs.Count}…");

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

                if (_cancelRequested || _nextJobIndex >= _jobs.Count)
                {
                    CompleteSession();
                    return;
                }

                // This is the critical boundary: the document open, workset inspection, link inspection,
                // NWC export, and close all run inside IExternalEventHandler.Execute.
                var processor = new JobProcessor(application.Application, _settings, _logger);
                JobExecutionResult jobResult = processor.ProcessSingleJob(currentJob, () => _cancelRequested, PublishProgress);

                if (jobResult.Succeeded)
                {
                    _summary.Successful++;
                }
                else if (jobResult.Skipped)
                {
                    _summary.Skipped++;
                }
                else if (jobResult.Cancelled)
                {
                    _summary.Cancelled++;
                }
                else
                {
                    _summary.Failed++;
                    _summary.FailedModels.Add($"{jobResult.ModelName}: {jobResult.ErrorMessage}");
                }

                _nextJobIndex++;
                PublishProgress(modelName, currentJob.LastStatus);

                if (_cancelRequested || _nextJobIndex >= _jobs.Count)
                {
                    CompleteSession();
                    return;
                }

                // Queue the next ExternalEvent raise on the modeless WPF dispatcher only after this
                // Execute call returns to Revit. This prevents long-running work on a background thread.
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

        private void RaiseNextJob()
        {
            if (IsSessionRunning && !_cancelRequested && _externalEvent != null)
            {
                _externalEvent.Raise();
            }
        }

        private void PublishProgress(string modelName, string stage)
        {
            // Use BeginInvoke to ensure UI updates are dispatched to the UI thread
            // and don't block the Revit execution thread.
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

            IsSessionRunning = false;
            _summary.Duration = DateTime.Now - _sessionStartedAt;
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
                // Format: acc://ModelName.rvt|URN
                string temp = path.Substring(6);
                int pipeIndex = temp.IndexOf('|');
                return pipeIndex > 0 ? temp.Substring(0, pipeIndex) : temp;
            }
            return System.IO.Path.GetFileName(path);
        }
    }
}
