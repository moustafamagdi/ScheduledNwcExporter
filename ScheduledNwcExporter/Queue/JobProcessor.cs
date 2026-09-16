using System;
using System.IO;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Core;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Revit;
using ScheduledNwcExporter.Reliability;

namespace ScheduledNwcExporter.Queue
{
    public sealed class JobExecutionResult
    {
        public string ModelName { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public bool Skipped { get; set; }
        public bool SkippedExisting { get; set; }
        public bool Cancelled { get; set; }
        public bool Retryable { get; set; }
        public bool OutputWritten { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Performs exactly one Revit-side attempt. Retry timing/orchestration belongs to the queue
    /// handler so the Revit UI thread is never held inside Thread.Sleep between attempts.
    /// </summary>
    public sealed class JobProcessor
    {
        private readonly Autodesk.Revit.ApplicationServices.Application _application;
        private readonly AppSettings _settings;
        private readonly ILogger _logger;
        private readonly DocumentManager _documentManager;
        private readonly WorksetManager _worksetManager;
        private readonly LinkManager _linkManager;
        private readonly NwcExporterService _nwcExporter;
        private readonly TemporaryModelCopyService _temporaryModelCopyService;
        private readonly ExportViewService _exportViewService;

        public JobProcessor(Autodesk.Revit.ApplicationServices.Application application, AppSettings settings, ILogger logger)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _documentManager = new DocumentManager(_logger);
            _worksetManager = new WorksetManager(_logger);
            _linkManager = new LinkManager(_logger);
            _nwcExporter = new NwcExporterService(_logger);
            _temporaryModelCopyService = new TemporaryModelCopyService(_logger);
            _exportViewService = new ExportViewService(_logger);
        }

        public JobExecutionResult ProcessSingleAttempt(ModelExportJob job, Func<bool> isCancellationRequested, int attemptNumber, Action<string, string>? onProgress = null)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (isCancellationRequested == null) throw new ArgumentNullException(nameof(isCancellationRequested));

            bool isCloud = job.SourceModelPath.StartsWith("acc://", StringComparison.OrdinalIgnoreCase);
            string modelName = isCloud ? job.SourceModelPath.Substring(6).Split('|')[0] : Path.GetFileName(job.SourceModelPath);
            DateTime startedAt = DateTime.Now;
            var result = new JobExecutionResult { ModelName = modelName };

            void UpdateProgress(JobStatus status, string stage, int percentage)
            {
                job.Status = status;
                job.CurrentStage = stage;
                job.ProgressPercentage = percentage;
                onProgress?.Invoke(modelName, stage);
            }

            if (!job.IsEnabled)
            {
                result.Skipped = true;
                result.ErrorMessage = "Job is disabled.";
                return CompleteAttempt(result, startedAt);
            }

            if (isCloud && job.CloudOpenAccessDenied)
            {
                result.Skipped = true;
                result.ErrorMessage = "Cloud model is blocked after Revit previously denied access. Re-select the cloud model after an administrator grants the required access.";
                result.Retryable = false;
                return CompleteAttempt(result, startedAt);
            }

            if (isCancellationRequested())
            {
                result.Cancelled = true;
                result.ErrorMessage = "Cancelled before beginning the next safe operation.";
                return CompleteAttempt(result, startedAt);
            }

            Document? document = null;
            PreparedModelSource? preparedModel = null;
            try
            {
                UpdateProgress(JobStatus.Processing, $"Validating (attempt {attemptNumber})", 5);
                ValidateJobInputs(job);
                ValidateCloudSession(job);

                ExportSettings exportSettings = job.CustomExportSettings ?? _settings.Export;

                UpdateProgress(JobStatus.Processing, "Preparing model", 10);
                preparedModel = _temporaryModelCopyService.Prepare(
                    job.SourceModelPath,
                    exportSettings.UseTemporaryCopyWithoutRevitLinks,
                    modelName);

                UpdateProgress(JobStatus.Processing, "Opening model", 20);
                document = _documentManager.OpenModelDetached(_application, preparedModel.OpenPath);
                if (document == null)
                    throw new InvalidOperationException("Revit could not open the source model as a detached document.");

                UpdateProgress(JobStatus.Processing, "Verifying worksets", 40);
                if (!_worksetManager.VerifyAllUserWorksetsOpen(document, modelName))
                    throw new InvalidOperationException("One or more required user worksets were closed after the document opened.");

                UpdateProgress(JobStatus.Processing, "Inspecting links", 50);
                _linkManager.InspectAndLogRevitLinks(document, modelName);

                if (isCancellationRequested())
                {
                    result.Cancelled = true;
                    result.ErrorMessage = "Cancelled before NWC export began.";
                    return CompleteAttempt(result, startedAt);
                }

                UpdateProgress(JobStatus.Processing, "Preparing export view", 60);
                ElementId? exportViewId = _exportViewService.GetOrCreateExportView(document, modelName);

                UpdateProgress(JobStatus.Processing, "Exporting NWC", 70);
                string outputFileName = ResolveFilenameTemplate(job.OutputFileNameTemplate, modelName);
                NwcExportResult exportResult = _nwcExporter.ExportModelToNwc(
                    document,
                    job.OutputDirectory,
                    outputFileName,
                    exportSettings,
                    exportViewId,
                    modelName);

                result.OutputPath = exportResult.OutputPath;

                if (exportResult.Outcome == NwcExportOutcome.SkippedExisting)
                {
                    result.Skipped = true;
                    result.SkippedExisting = true;
                    result.ErrorMessage = "Existing NWC retained because overwrite policy is Skip; freshness was not advanced.";
                    result.Retryable = false;
                    return CompleteAttempt(result, startedAt);
                }

                if (!exportResult.Succeeded || !exportResult.WroteOutput)
                    throw new InvalidOperationException("The NWC exporter did not create a valid output file.");

                result.Succeeded = true;
                result.OutputWritten = true;
                result.Retryable = false;

                // Freshness is advanced only after the exporter verified a real non-empty NWC file.
                ExportStateStore.RecordSuccessfulExport(job, _settings, exportResult.OutputPath);
                return CompleteAttempt(result, startedAt);
            }
            catch (CloudModelAccessDeniedException ex)
            {
                result.ErrorMessage = ex.Message;
                result.Retryable = false;
                if (ex.IsPermanentAccessDenial)
                {
                    job.CloudOpenAccessDenied = true;
                    job.CloudOpenAccessDeniedAt = DateTime.Now;
                    _logger.Error("Job", "Cloud model access was denied by Revit. Future unattended attempts are blocked until the model is re-selected.", modelName, "CloudAccessDenied", ex);
                }
                else
                {
                    _logger.Warning("Job", "Cloud preflight stopped because no Autodesk session token was available.", modelName, "CloudAccessPreflight", ex);
                }
                return CompleteAttempt(result, startedAt);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                result.Retryable = !(ex is FileNotFoundException || ex is ArgumentException || ex is DirectoryNotFoundException);
                _logger.Error("Job", $"Export attempt {attemptNumber} failed: {ex.Message}", modelName, "Exporting", ex);
                return CompleteAttempt(result, startedAt);
            }
            finally
            {
                if (document != null)
                {
                    UpdateProgress(JobStatus.Processing, "Closing model", 95);
                    _documentManager.CloseDocumentSafely(document);
                }
                preparedModel?.Dispose();
            }
        }

        public JobExecutionResult FinalizeJob(ModelExportJob job, JobExecutionResult result, Action<string, string>? onProgress = null)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (result == null) throw new ArgumentNullException(nameof(result));

            job.LastRun = DateTime.Now;
            var runResult = new RunResult
            {
                Timestamp = job.LastRun.Value,
                Duration = result.Duration.ToString(@"hh\:mm\:ss"),
                ErrorMessage = result.ErrorMessage
            };

            void SetFinal(JobStatus status, string stage)
            {
                job.Status = status;
                job.CurrentStage = stage;
                job.ProgressPercentage = 100;
                onProgress?.Invoke(result.ModelName, stage);
            }

            if (result.Succeeded)
            {
                SetFinal(JobStatus.Success, "Success");
                job.LastError = string.Empty;
                runResult.Status = JobStatus.Success;
                _logger.Success("Job", $"Job completed in {runResult.Duration}.", result.ModelName, "Completed");
            }
            else if (result.Cancelled)
            {
                SetFinal(JobStatus.Cancelled, "Cancelled");
                job.LastError = result.ErrorMessage;
                runResult.Status = JobStatus.Cancelled;
                _logger.Warning("Job", "Job cancelled at a safe boundary.", result.ModelName, "Cancelled");
            }
            else if (result.Skipped)
            {
                string stage = result.SkippedExisting ? "Existing output skipped" : "Skipped";
                SetFinal(JobStatus.Skipped, stage);
                job.LastError = result.ErrorMessage;
                runResult.Status = JobStatus.Skipped;
                _logger.Info("Job", result.ErrorMessage, result.ModelName, stage);
            }
            else
            {
                string finalStage = job.CloudOpenAccessDenied
                    ? "Cloud access denied"
                    : result.ErrorMessage.IndexOf("session token", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "Autodesk sign-in required"
                        : "Failed";
                SetFinal(JobStatus.Failed, finalStage);
                job.LastError = result.ErrorMessage;
                runResult.Status = JobStatus.Failed;
                _logger.Error("Job", $"Job permanently failed. Reason: {result.ErrorMessage}", result.ModelName, "Failed");
            }

            job.AddRunResult(runResult);
            return result;
        }

        private static JobExecutionResult CompleteAttempt(JobExecutionResult result, DateTime startedAt)
        {
            result.Duration = DateTime.Now - startedAt;
            return result;
        }

        private static void ValidateCloudSession(ModelExportJob job)
        {
            if (!job.IsCloud) return;

            string token = CloudAuthenticationService.GetAccessToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new CloudModelAccessDeniedException(
                    "No active Autodesk session token is available. Sign in to Autodesk in Revit before exporting cloud models.",
                    new InvalidOperationException("Autodesk session token was unavailable."),
                    isPermanentAccessDenial: false);
            }
        }

        private static void ValidateJobInputs(ModelExportJob job)
        {
            if (string.IsNullOrWhiteSpace(job.SourceModelPath))
                throw new ArgumentException("The source model path is empty.");

            if (!job.SourceModelPath.ToLowerInvariant().Contains(".rvt"))
                throw new ArgumentException("The source model must have a .rvt extension.");

            bool isCloud = job.SourceModelPath.StartsWith("acc://", StringComparison.OrdinalIgnoreCase);
            if (!isCloud && !File.Exists(job.SourceModelPath))
                throw new FileNotFoundException("Source model file was not found.", job.SourceModelPath);

            if (string.IsNullOrWhiteSpace(job.OutputDirectory))
                throw new ArgumentException("The output directory is empty.");

            if (string.IsNullOrWhiteSpace(job.OutputFileNameTemplate))
                throw new ArgumentException("The output filename template is empty.");
        }

        private static string ResolveFilenameTemplate(string template, string modelFileName)
        {
            string modelName = Path.GetFileNameWithoutExtension(modelFileName);
            DateTime now = DateTime.Now;
            string resolved = template
                .Replace("{ModelName}", modelName)
                .Replace("{ModelFileName}", modelFileName)
                .Replace("{Date}", now.ToString("yyyy-MM-dd"))
                .Replace("{Time}", now.ToString("HH-mm-ss"))
                .Replace("{Year}", now.ToString("yyyy"))
                .Replace("{Month}", now.ToString("MM"))
                .Replace("{Day}", now.ToString("dd"))
                .Replace("{Hour}", now.ToString("HH"))
                .Replace("{Minute}", now.ToString("mm"));

            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
                resolved = resolved.Replace(invalidCharacter, '_');

            return resolved.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase) ? resolved : resolved + ".nwc";
        }
    }
}
