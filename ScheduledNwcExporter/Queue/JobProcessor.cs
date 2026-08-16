using System;
using System.IO;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Configuration;
using ScheduledNwcExporter.Logging;
using ScheduledNwcExporter.Revit;

namespace ScheduledNwcExporter.Queue
{
    /// <summary>
    /// Represents the final outcome of one export job.
    /// </summary>
    public sealed class JobExecutionResult
    {
        public string ModelName { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public bool Skipped { get; set; }
        public bool Cancelled { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Processes one model export at a time. The caller is responsible for invoking this class only
    /// from a valid Revit API context, such as IExternalEventHandler.Execute.
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

        /// <summary>
        /// Executes one export job, isolates failures, and always closes the programmatically opened document.
        /// This method must never be invoked through Task.Run, a timer callback, or another background thread.
        /// </summary>
        public JobExecutionResult ProcessSingleJob(ModelExportJob job, Func<bool> isCancellationRequested)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (isCancellationRequested == null) throw new ArgumentNullException(nameof(isCancellationRequested));

            string modelName = Path.GetFileName(job.SourceModelPath);
            DateTime startedAt = DateTime.Now;
            var result = new JobExecutionResult { ModelName = modelName };

            if (!job.IsEnabled)
            {
                result.Skipped = true;
                job.LastStatus = "Skipped";
                _logger.Info("Job", "Disabled job skipped.", modelName, "Preflight");
                return CompleteJob(job, result, startedAt);
            }

            int maximumAttempts = Math.Max(1, job.RetryCount + 1);
            string lastError = string.Empty;

            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                if (isCancellationRequested())
                {
                    result.Cancelled = true;
                    result.ErrorMessage = "Cancelled before beginning the next safe operation.";
                    _logger.Warning("Job", result.ErrorMessage, modelName, "Cancellation");
                    break;
                }

                if (attempt > 1)
                {
                    _logger.Warning("Job", $"Retrying job (attempt {attempt} of {maximumAttempts}).", modelName, "Retrying");
                }

                Document? document = null;
                PreparedModelSource? preparedModel = null;
                try
                {
                    job.LastStatus = $"Attempt {attempt} - Validating";
                    ValidateJobInputs(job);

                    job.LastStatus = $"Attempt {attempt} - Preparing model";
                    preparedModel = _temporaryModelCopyService.Prepare(
                        job.SourceModelPath,
                        _settings.Export.UseTemporaryCopyWithoutRevitLinks,
                        modelName);

                    job.LastStatus = $"Attempt {attempt} - Opening model";
                    document = _documentManager.OpenModelDetached(_application, preparedModel.OpenPath);
                    if (document == null)
                    {
                        throw new InvalidOperationException("Revit could not open the source model as a detached document.");
                    }

                    job.LastStatus = "Verifying worksets";
                    if (!_worksetManager.VerifyAllUserWorksetsOpen(document, modelName))
                    {
                        throw new InvalidOperationException("One or more required user worksets were closed after the document opened.");
                    }

                    job.LastStatus = "Inspecting links";
                    _linkManager.InspectAndLogRevitLinks(document, modelName);

                    if (isCancellationRequested())
                    {
                        result.Cancelled = true;
                        result.ErrorMessage = "Cancelled before NWC export began.";
                        _logger.Warning("Job", result.ErrorMessage, modelName, "Cancellation");
                        break;
                    }

                    job.LastStatus = "Preparing export view";
                    ElementId? exportViewId = _exportViewService.GetOrCreateExportView(document, modelName);

                    job.LastStatus = "Exporting NWC";
                    string outputFileName = ResolveFilenameTemplate(job.OutputFileNameTemplate, modelName);
                    if (!_nwcExporter.ExportModelToNwc(document, job.OutputDirectory, outputFileName, _settings.Export, exportViewId, modelName))
                    {
                        throw new InvalidOperationException("The NWC exporter did not create a valid output file.");
                    }

                    result.Succeeded = true;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    _logger.Error("Job", $"Export attempt {attempt} failed: {ex.Message}", modelName, "Exporting", ex);

                    if (ex is FileNotFoundException || ex is ArgumentException || ex is DirectoryNotFoundException)
                    {
                        break;
                    }
                }
                finally
                {
                    if (document != null)
                    {
                        job.LastStatus = "Closing model";
                        _documentManager.CloseDocumentSafely(document);
                    }

                    preparedModel?.Dispose();
                }
            }

            result.ErrorMessage = result.Succeeded || result.Cancelled ? result.ErrorMessage : lastError;
            return CompleteJob(job, result, startedAt);
        }

        private JobExecutionResult CompleteJob(ModelExportJob job, JobExecutionResult result, DateTime startedAt)
        {
            result.Duration = DateTime.Now - startedAt;
            job.LastRun = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            job.LastDuration = result.Duration.ToString(@"hh\:mm\:ss");

            if (result.Succeeded)
            {
                job.LastStatus = "Success";
                job.LastError = string.Empty;
                _logger.Success("Job", $"Job completed in {job.LastDuration}.", result.ModelName, "Completed");
            }
            else if (result.Cancelled)
            {
                job.LastStatus = "Cancelled";
                job.LastError = result.ErrorMessage;
                _logger.Warning("Job", "Job cancelled at a safe boundary.", result.ModelName, "Cancelled");
            }
            else if (result.Skipped)
            {
                job.LastStatus = "Skipped";
            }
            else
            {
                job.LastStatus = "Failed";
                job.LastError = result.ErrorMessage;
                _logger.Error("Job", $"Job permanently failed. Reason: {result.ErrorMessage}", result.ModelName, "Failed");
            }

            return result;
        }

        private static void ValidateJobInputs(ModelExportJob job)
        {
            if (string.IsNullOrWhiteSpace(job.SourceModelPath))
            {
                throw new ArgumentException("The source model path is empty.");
            }

            if (!job.SourceModelPath.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The source model must have a .rvt extension.");
            }

            if (!File.Exists(job.SourceModelPath))
            {
                throw new FileNotFoundException("Source model file was not found.", job.SourceModelPath);
            }

            if (string.IsNullOrWhiteSpace(job.OutputDirectory))
            {
                throw new ArgumentException("The output directory is empty.");
            }

            if (string.IsNullOrWhiteSpace(job.OutputFileNameTemplate))
            {
                throw new ArgumentException("The output filename template is empty.");
            }
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
            {
                resolved = resolved.Replace(invalidCharacter, '_');
            }

            return resolved.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase) ? resolved : resolved + ".nwc";
        }
    }
}
